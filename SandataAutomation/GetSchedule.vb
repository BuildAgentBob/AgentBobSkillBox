Try

    errorMessage = ""
    scheduleHtml = ""

    rawFormData =
        New List(Of KeyValuePair(Of String, String))()

    Dim baseUrl As String =
        "https://us.sandata.com"

    Dim scheduleUrl As String =
        baseUrl &
        "/Scheduling/AddEditSchedule.aspx?schedID=" &
        Uri.EscapeDataString(scheduleId)

    Console.WriteLine(
        "Loading schedule: " & scheduleUrl
    )

    ' ============================================================
    ' GET SCHEDULE PAGE
    ' ============================================================

    Dim request As HttpWebRequest =
        CType(WebRequest.Create(scheduleUrl), HttpWebRequest)

    request.Method = "GET"
    request.CookieContainer = cookies
    request.AllowAutoRedirect = True
    request.KeepAlive = True

    request.UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
        "AppleWebKit/537.36 (KHTML, like Gecko) " &
        "Chrome/150.0.0.0 Safari/537.36"

    request.Accept =
        "text/html,application/xhtml+xml,application/xml;q=0.9," &
        "image/avif,image/webp,image/apng,*/*;q=0.8," &
        "application/signed-exchange;v=b3;q=0.7"

    request.Headers.Add(
        HttpRequestHeader.AcceptLanguage,
        "en-US,en;q=0.9"
    )

    request.Headers.Add(
        HttpRequestHeader.CacheControl,
        "no-cache"
    )

    request.Headers.Add(
        "Pragma",
        "no-cache"
    )

    request.Headers.Add(
        "Upgrade-Insecure-Requests",
        "1"
    )

    Using response As HttpWebResponse =
        CType(request.GetResponse(), HttpWebResponse)

        Console.WriteLine(
            "GET status: " &
            CInt(response.StatusCode).ToString() &
            " " &
            response.StatusDescription
        )

        Using reader As New StreamReader(
            response.GetResponseStream()
        )

            scheduleHtml = reader.ReadToEnd()

        End Using

    End Using

    If String.IsNullOrWhiteSpace(scheduleHtml) Then
        Throw New Exception(
            "The schedule page returned empty HTML."
        )
    End If

    Console.WriteLine(
        "Schedule HTML length: " &
        scheduleHtml.Length.ToString()
    )

    ' ============================================================
    ' ATTRIBUTE READER
    ' ============================================================

    Dim GetAttribute As Func(Of String, String, String) =
        Function(htmlTag As String,
                 attributeName As String) As String

            Dim pattern As String =
                "(?<![\w-])" &
                Regex.Escape(attributeName) &
                "\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))"

            Dim attributeMatch As Match =
                Regex.Match(
                    htmlTag,
                    pattern,
                    RegexOptions.IgnoreCase Or
                    RegexOptions.Singleline
                )

            If Not attributeMatch.Success Then
                Return ""
            End If

            Dim attributeValue As String = ""

            If attributeMatch.Groups(1).Success Then

                attributeValue =
                    attributeMatch.Groups(1).Value

            ElseIf attributeMatch.Groups(2).Success Then

                attributeValue =
                    attributeMatch.Groups(2).Value

            ElseIf attributeMatch.Groups(3).Success Then

                attributeValue =
                    attributeMatch.Groups(3).Value

            End If

            Return WebUtility.HtmlDecode(attributeValue)

        End Function

    Dim HasAttribute As Func(Of String, String, Boolean) =
        Function(htmlTag As String,
                 attributeName As String) As Boolean

            Return Regex.IsMatch(
                htmlTag,
                "(?<![\w-])" &
                Regex.Escape(attributeName) &
                "(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+))?" &
                "(?=\s|/?>)",
                RegexOptions.IgnoreCase Or
                RegexOptions.Singleline
            )

        End Function

    ' ============================================================
    ' EXTRACT INPUT CONTROLS
    ' ============================================================

    Dim inputMatches As MatchCollection =
        Regex.Matches(
            scheduleHtml,
            "<input\b[^>]*>",
            RegexOptions.IgnoreCase Or
            RegexOptions.Singleline
        )

    For Each inputMatch As Match In inputMatches

        Dim inputTag As String =
            inputMatch.Value

        Dim fieldName As String =
            GetAttribute(inputTag, "name")

        If String.IsNullOrWhiteSpace(fieldName) Then
            Continue For
        End If

        If HasAttribute(inputTag, "disabled") Then
            Continue For
        End If

        Dim inputType As String =
            GetAttribute(inputTag, "type").
            Trim().
            ToLowerInvariant()

        If String.IsNullOrWhiteSpace(inputType) Then
            inputType = "text"
        End If

        Select Case inputType

            Case "checkbox", "radio"

                If Not HasAttribute(inputTag, "checked") Then
                    Continue For
                End If

            Case "submit", "button", "reset", "image", "file"

                ' The clicked Save button will be added during submit.
                Continue For

        End Select

        Dim fieldValue As String =
            GetAttribute(inputTag, "value")

        If (inputType = "checkbox" OrElse
            inputType = "radio") AndAlso
            String.IsNullOrEmpty(fieldValue) Then

            fieldValue = "on"

        End If

        rawFormData.Add(
            New KeyValuePair(Of String, String)(
                fieldName,
                fieldValue
            )
        )

    Next

    Console.WriteLine(
        "Input controls extracted: " &
        rawFormData.Count.ToString()
    )

    ' ============================================================
    ' EXTRACT TEXTAREA CONTROLS
    ' ============================================================

    Dim textareaMatches As MatchCollection =
        Regex.Matches(
            scheduleHtml,
            "<textarea\b[^>]*>.*?</textarea>",
            RegexOptions.IgnoreCase Or
            RegexOptions.Singleline
        )

    For Each textareaMatch As Match In textareaMatches

        Dim textareaTag As String =
            textareaMatch.Value

        Dim fieldName As String =
            GetAttribute(textareaTag, "name")

        If String.IsNullOrWhiteSpace(fieldName) Then
            Continue For
        End If

        If HasAttribute(textareaTag, "disabled") Then
            Continue For
        End If

        Dim valueMatch As Match =
            Regex.Match(
                textareaTag,
                "<textarea\b[^>]*>(.*?)</textarea>",
                RegexOptions.IgnoreCase Or
                RegexOptions.Singleline
            )

        Dim fieldValue As String = ""

        If valueMatch.Success Then

            fieldValue =
                WebUtility.HtmlDecode(
                    valueMatch.Groups(1).Value
                )

        End If

        rawFormData.Add(
            New KeyValuePair(Of String, String)(
                fieldName,
                fieldValue
            )
        )

    Next

    ' ============================================================
    ' EXTRACT SELECT CONTROLS
    ' ============================================================

    Dim selectMatches As MatchCollection =
        Regex.Matches(
            scheduleHtml,
            "<select\b[^>]*>.*?</select>",
            RegexOptions.IgnoreCase Or
            RegexOptions.Singleline
        )

    For Each selectMatch As Match In selectMatches

        Dim selectTag As String =
            selectMatch.Value

        Dim fieldName As String =
            GetAttribute(selectTag, "name")

        If String.IsNullOrWhiteSpace(fieldName) Then
            Continue For
        End If

        Dim optionMatches As MatchCollection =
            Regex.Matches(
                selectTag,
                "<option\b[^>]*>.*?</option>",
                RegexOptions.IgnoreCase Or
                RegexOptions.Singleline
            )

        Dim selectedOptions As New List(Of String)()

        For Each optionMatch As Match In optionMatches

            Dim optionTag As String =
                optionMatch.Value

            If HasAttribute(optionTag, "selected") Then

                Dim optionValue As String =
                    GetAttribute(optionTag, "value")

                If String.IsNullOrEmpty(optionValue) Then

                    Dim optionTextMatch As Match =
                        Regex.Match(
                            optionTag,
                            "<option\b[^>]*>(.*?)</option>",
                            RegexOptions.IgnoreCase Or
                            RegexOptions.Singleline
                        )

                    If optionTextMatch.Success Then

                        optionValue =
                            WebUtility.HtmlDecode(
                                Regex.Replace(
                                    optionTextMatch.Groups(1).Value,
                                    "<[^>]+>",
                                    ""
                                )
                            ).Trim()

                    End If

                End If

                selectedOptions.Add(optionValue)

            End If

        Next

        ' If no option explicitly has selected,
        ' the browser normally uses the first option.
        If selectedOptions.Count = 0 AndAlso
           optionMatches.Count > 0 Then

            Dim firstOptionTag As String =
                optionMatches(0).Value

            Dim firstOptionValue As String =
                GetAttribute(firstOptionTag, "value")

            If String.IsNullOrEmpty(firstOptionValue) Then

                Dim optionTextMatch As Match =
                    Regex.Match(
                        firstOptionTag,
                        "<option\b[^>]*>(.*?)</option>",
                        RegexOptions.IgnoreCase Or
                        RegexOptions.Singleline
                    )

                If optionTextMatch.Success Then

                    firstOptionValue =
                        WebUtility.HtmlDecode(
                            Regex.Replace(
                                optionTextMatch.Groups(1).Value,
                                "<[^>]+>",
                                ""
                            )
                        ).Trim()

                End If

            End If

            selectedOptions.Add(firstOptionValue)

        End If

        For Each selectedValue As String In selectedOptions

            rawFormData.Add(
                New KeyValuePair(Of String, String)(
                    fieldName,
                    selectedValue
                )
            )

        Next

    Next

    ' ============================================================
    ' ADD SCHEDULE ID
    ' ============================================================

    rawFormData.Insert(
        0,
        New KeyValuePair(Of String, String)(
            "schedID",
            scheduleId
        )
    )

    ' ============================================================
    ' VALIDATE WEBFORMS STATE
    ' ============================================================

    Dim viewStateValue As String =
        rawFormData.
        Where(
            Function(item)
                Return item.Key = "__VIEWSTATE"
            End Function
        ).
        Select(
            Function(item)
                Return item.Value
            End Function
        ).
        FirstOrDefault()

    If String.IsNullOrWhiteSpace(viewStateValue) Then

        Throw New Exception(
            "Unable to extract __VIEWSTATE from the schedule page."
        )

    End If

    Dim eventValidationValue As String =
        rawFormData.
        Where(
            Function(item)
                Return item.Key = "__EVENTVALIDATION"
            End Function
        ).
        Select(
            Function(item)
                Return item.Value
            End Function
        ).
        FirstOrDefault()

    If String.IsNullOrWhiteSpace(eventValidationValue) Then

        Console.WriteLine(
            "Warning: __EVENTVALIDATION was not found."
        )

    End If

    ' ============================================================
    ' DISPLAY DUPLICATE FIELDS
    ' ============================================================

    Dim duplicateGroups =
        rawFormData.
        GroupBy(
            Function(item)
                Return item.Key
            End Function
        ).
        Where(
            Function(Group)
                Return Group.Count() > 1
            End Function
        ).
        ToList()

    For Each duplicateGroup In duplicateGroups

        Console.WriteLine(
            "Duplicate field: " &
            duplicateGroup.Key &
            " | Count: " &
            duplicateGroup.Count().ToString()
        )

        Dim duplicateItems =
            duplicateGroup.ToList()

        For index As Integer = 0 To duplicateItems.Count - 1

            Console.WriteLine(
                "    Position " &
                (index + 1).ToString() &
                " = " &
                duplicateItems(index).Value
            )

        Next

    Next

    Console.WriteLine(
        "Total form entries extracted: " &
        rawFormData.Count.ToString()
    )

    Console.WriteLine(
        "GetSchedule completed successfully for schedule ID: " &
        scheduleId
    )

Catch ex As Exception

    errorMessage =
        ex.Message

    Console.WriteLine(
        "GetSchedule failed: " &
        ex.ToString()
    )

End Try