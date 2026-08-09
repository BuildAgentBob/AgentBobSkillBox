Try

    ' ============================================================
    ' INITIALISE OUTPUTS
    ' ============================================================

    errorMessage = ""
    prepareSuccessful = False

    updatedFormData = New List(Of System.Collections.Generic.KeyValuePair(Of String, String))()

    If cookies Is Nothing Then
        Throw New Exception("The Sandata CookieContainer is Nothing.")
    End If

    Dim localCookies As System.Net.CookieContainer = cookies

    If String.IsNullOrWhiteSpace(scheduleID) Then
        Throw New Exception("Schedule ID is required.")
    End If

    If String.IsNullOrWhiteSpace(status) Then
        Throw New Exception("Status is required.")
    End If

    If String.IsNullOrWhiteSpace(reasonCode) Then
        Throw New Exception("Reason code is required.")
    End If


    ' ============================================================
    ' NORMALISE INPUTS
    ' ============================================================

    Dim statusValue As String = status.Trim()

    Select Case statusValue.ToLowerInvariant()

        Case "pending"
            statusValue = "01"

        Case "confirmed"
            statusValue = "02"

        Case "in-process", "in process"
            statusValue = "03"

        Case "closed"
            statusValue = "04"

        Case "hold"
            statusValue = "09"

        Case "cancelled", "canceled"
            statusValue = "10"

    End Select

    Dim validStatusCodes As String() = New String() {"01", "02", "03", "04", "09", "10"}

    If Not validStatusCodes.Contains(statusValue) Then
        Throw New Exception("Invalid Sandata status: " & statusValue)
    End If

    Dim commentValue As String = If(comment, "").Trim()
    Dim reasonCodeValue As String = reasonCode.Trim()
    Dim exceptionCategoryValue As String = If(sandataExceptionCategory, "").Trim()


    ' ============================================================
    ' URLS
    ' ============================================================

    Dim baseUrl As String = "https://us.sandata.com"
    Dim scheduleUrl As String = baseUrl & "/Scheduling/AddEditSchedule.aspx?schedID=" & System.Uri.EscapeDataString(scheduleID.Trim())


    ' ============================================================
    ' GET HELPER
    ' ============================================================

    Dim DoGet As Func(Of String, String, String) =
        Function(url As String, referer As String) As String

            Console.WriteLine("GET " & url)

            Dim request As System.Net.HttpWebRequest =
                CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)

            request.Method = "GET"
            request.CookieContainer = localCookies
            request.AllowAutoRedirect = True
            request.KeepAlive = True
            request.Timeout = 120000
            request.ReadWriteTimeout = 120000

            request.AutomaticDecompression =
                System.Net.DecompressionMethods.GZip Or System.Net.DecompressionMethods.Deflate

            request.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
                "AppleWebKit/537.36 (KHTML, like Gecko) " &
                "Chrome/150.0.0.0 Safari/537.36"

            request.Accept =
                "text/html,application/xhtml+xml," &
                "application/xml;q=0.9," &
                "image/avif,image/webp,image/apng,*/*;q=0.8"

            If Not String.IsNullOrWhiteSpace(referer) Then
                request.Referer = referer
            End If

            Using response As System.Net.HttpWebResponse =
                CType(request.GetResponse(), System.Net.HttpWebResponse)

                Dim finalGetUrl As String = response.ResponseUri.AbsoluteUri

                Console.WriteLine("GET status: " & CInt(response.StatusCode).ToString())
                Console.WriteLine("GET final URL: " & finalGetUrl)

                Using reader As New System.IO.StreamReader(response.GetResponseStream())

                    Dim html As String = reader.ReadToEnd()

                    If finalGetUrl.IndexOf("/Security/Login", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        Throw New Exception("Sandata session expired. GET redirected to: " & finalGetUrl)
                    End If

                    Return html

                End Using

            End Using

        End Function


    ' ============================================================
    ' GET SCHEDULE PAGE
    ' ============================================================

    Console.WriteLine("Getting Sandata schedule: " & scheduleUrl)

    Dim pageHtml As String = DoGet(scheduleUrl, baseUrl & "/Scheduling/")

    If String.IsNullOrWhiteSpace(pageHtml) Then
        Throw New Exception("Sandata returned an empty schedule page.")
    End If

    If pageHtml.IndexOf("__VIEWSTATE", StringComparison.OrdinalIgnoreCase) < 0 Then
        Throw New Exception("The schedule page did not contain __VIEWSTATE.")
    End If


    ' ============================================================
    ' HTML ATTRIBUTE HELPER
    ' ============================================================

    Dim GetAttribute As Func(Of String, String, String) =
        Function(tagHtml As String, attributeName As String) As String

            If String.IsNullOrWhiteSpace(tagHtml) Then
                Return ""
            End If

            Dim attributePattern As String =
                "(?is)\b" &
                System.Text.RegularExpressions.Regex.Escape(attributeName) &
                "\s*=\s*" &
                "(?:""(?<value>[^""]*)""|" &
                "'(?<value>[^']*)'|" &
                "(?<value>[^\s>]+))"

            Dim attributeMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(tagHtml, attributePattern)

            If Not attributeMatch.Success Then
                Return ""
            End If

            Return System.Net.WebUtility.HtmlDecode(attributeMatch.Groups("value").Value)

        End Function


    ' ============================================================
    ' DISABLED HELPER
    ' ============================================================

    Dim IsDisabled As Func(Of String, Boolean) =
        Function(tagHtml As String) As Boolean

            Return System.Text.RegularExpressions.Regex.IsMatch(
                tagHtml,
                "(?is)\bdisabled" &
                "(?:\s*=\s*" &
                "(?:""disabled""|'disabled'|disabled))?"
            )

        End Function


    ' ============================================================
    ' EXTRACT JAVASCRIPT VARIABLE
    ' ============================================================

    Dim GetJavascriptString As Func(Of String, String) =
        Function(variableName As String) As String

            Dim variableMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    pageHtml,
                    "(?is)\bvar\s+" &
                    System.Text.RegularExpressions.Regex.Escape(variableName) &
                    "\s*=\s*'(?<value>(?:\\.|[^'])*)'\s*;"
                )

            If Not variableMatch.Success Then
                Return ""
            End If

            Dim value As String = variableMatch.Groups("value").Value

            value = value.Replace("\'", "'")
            value = value.Replace("\/", "/")
            value = value.Replace("\\", "\")

            Return System.Net.WebUtility.HtmlDecode(value)

        End Function


    ' ============================================================
    ' JSON STRING VALUE HELPER
    ' ============================================================

    Dim GetJsonValue As Func(Of String, String, String) =
        Function(jsonText As String, propertyName As String) As String

            If String.IsNullOrWhiteSpace(jsonText) Then
                Return ""
            End If

            Dim jsonMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    jsonText,
                    "(?is)""" &
                    System.Text.RegularExpressions.Regex.Escape(propertyName) &
                    """\s*:\s*" &
                    "(?:""(?<stringValue>(?:\\.|[^""])*)""|" &
                    "(?<rawValue>[^,\}\]]+))"
                )

            If Not jsonMatch.Success Then
                Return ""
            End If

            Dim result As String = ""

            If jsonMatch.Groups("stringValue").Success Then

                result = jsonMatch.Groups("stringValue").Value
                result = result.Replace("\""", """" )
                result = result.Replace("\/", "/")
                result = result.Replace("\\", "\")

            Else

                result = jsonMatch.Groups("rawValue").Value.Trim()

            End If

            Return System.Net.WebUtility.HtmlDecode(result)

        End Function


    ' ============================================================
    ' MONEY FORMAT HELPER
    ' ============================================================

    Dim FormatMoney As Func(Of String, String) =
        Function(rawValue As String) As String

            Dim cleanedValue As String = If(rawValue, "").Trim().Replace("$", "")

            Dim decimalValue As Decimal = 0D

            If Decimal.TryParse(
                cleanedValue,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                decimalValue
            ) Then

                Return "$" & decimalValue.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)

            End If

            Return "$0.00"

        End Function


    ' ============================================================
    ' EXTRACT FORM FIELDS IN ORIGINAL PAGE ORDER
    ' ============================================================

    Dim indexedFields As New List(
        Of Tuple(
            Of Integer,
            System.Collections.Generic.KeyValuePair(Of String, String)
        )
    )


    ' ============================================================
    ' EXTRACT INPUT CONTROLS
    ' ============================================================

    Dim inputMatches As System.Text.RegularExpressions.MatchCollection =
        System.Text.RegularExpressions.Regex.Matches(pageHtml, "(?is)<input\b[^>]*>")

    For Each inputMatch As System.Text.RegularExpressions.Match In inputMatches

        Dim inputTag As String = inputMatch.Value
        Dim inputName As String = GetAttribute(inputTag, "name")

        If String.IsNullOrWhiteSpace(inputName) Then
            Continue For
        End If

        Dim requiredDisabledInputs As String() =
            New String() {
                "ctl00$cphContent$txtActualIn",
                "ctl00$cphContent$txtActualOut",
                "ctl00$cphContent$txtActualHrs"
            }

        Dim includeDisabledInput As Boolean =
            requiredDisabledInputs.Any(
                Function(requiredName As String) As Boolean
                    Return String.Equals(inputName, requiredName, StringComparison.OrdinalIgnoreCase)
                End Function
            )

        If IsDisabled(inputTag) AndAlso Not includeDisabledInput Then
            Continue For
        End If

        Dim inputType As String = GetAttribute(inputTag, "type").Trim().ToLowerInvariant()

        If String.IsNullOrWhiteSpace(inputType) Then
            inputType = "text"
        End If

        If inputType = "submit" OrElse
           inputType = "button" OrElse
           inputType = "image" OrElse
           inputType = "reset" OrElse
           inputType = "file" Then

            Continue For

        End If

        If inputType = "checkbox" OrElse inputType = "radio" Then

            Dim isChecked As Boolean =
                System.Text.RegularExpressions.Regex.IsMatch(
                    inputTag,
                    "(?is)\bchecked" &
                    "(?:\s*=\s*" &
                    "(?:""checked""|'checked'|checked))?"
                )

            If Not isChecked Then
                Continue For
            End If

        End If

        Dim inputValue As String = GetAttribute(inputTag, "value")

        indexedFields.Add(
            Tuple.Create(
                inputMatch.Index,
                New System.Collections.Generic.KeyValuePair(Of String, String)(inputName, inputValue)
            )
        )

    Next


    ' ============================================================
    ' EXTRACT SELECT CONTROLS
    ' ============================================================

    Dim selectMatches As System.Text.RegularExpressions.MatchCollection =
        System.Text.RegularExpressions.Regex.Matches(pageHtml, "(?is)<select\b[^>]*>.*?</select>")

    For Each selectMatch As System.Text.RegularExpressions.Match In selectMatches

        Dim selectHtml As String = selectMatch.Value

        Dim selectStartTagMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(selectHtml, "(?is)^<select\b[^>]*>")

        If Not selectStartTagMatch.Success Then
            Continue For
        End If

        Dim selectName As String = GetAttribute(selectStartTagMatch.Value, "name")

        If String.IsNullOrWhiteSpace(selectName) Then
            Continue For
        End If

        Dim requiredDisabledSelects As String() =
            New String() {
                "ctl00$cphContent$ddlStatus",
                "ctl00$cphContent$ddlLocation",
                "ctl00$cphContent$ddlCompany",
                "ctl00$cphContent$ddlService"
            }

        Dim includeDisabledSelect As Boolean =
            requiredDisabledSelects.Any(
                Function(requiredName As String) As Boolean
                    Return String.Equals(selectName, requiredName, StringComparison.OrdinalIgnoreCase)
                End Function
            )

        If IsDisabled(selectStartTagMatch.Value) AndAlso Not includeDisabledSelect Then
            Continue For
        End If

        Dim selectedValue As String = ""
        Dim firstOptionValue As String = ""
        Dim firstOptionFound As Boolean = False
        Dim selectedOptionFound As Boolean = False

        Dim optionMatches As System.Text.RegularExpressions.MatchCollection =
            System.Text.RegularExpressions.Regex.Matches(selectHtml, "(?is)<option\b[^>]*>.*?</option>")

        For Each optionMatch As System.Text.RegularExpressions.Match In optionMatches

            Dim optionHtml As String = optionMatch.Value

            Dim optionStartTagMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(optionHtml, "(?is)^<option\b[^>]*>")

            If Not optionStartTagMatch.Success Then
                Continue For
            End If

            Dim optionValue As String = GetAttribute(optionStartTagMatch.Value, "value")

            If Not firstOptionFound Then
                firstOptionValue = optionValue
                firstOptionFound = True
            End If

            Dim isSelected As Boolean =
                System.Text.RegularExpressions.Regex.IsMatch(
                    optionStartTagMatch.Value,
                    "(?is)\bselected" &
                    "(?:\s*=\s*" &
                    "(?:""selected""|'selected'|selected))?"
                )

            If isSelected Then
                selectedValue = optionValue
                selectedOptionFound = True
                Exit For
            End If

        Next

        If Not selectedOptionFound Then
            selectedValue = firstOptionValue
        End If

        indexedFields.Add(
            Tuple.Create(
                selectMatch.Index,
                New System.Collections.Generic.KeyValuePair(Of String, String)(selectName, selectedValue)
            )
        )

    Next


    ' ============================================================
    ' EXTRACT TEXTAREA CONTROLS
    ' ============================================================

    Dim textareaMatches As System.Text.RegularExpressions.MatchCollection =
        System.Text.RegularExpressions.Regex.Matches(pageHtml, "(?is)<textarea\b[^>]*>.*?</textarea>")

    For Each textareaMatch As System.Text.RegularExpressions.Match In textareaMatches

        Dim textareaHtml As String = textareaMatch.Value

        Dim textareaStartTagMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(textareaHtml, "(?is)^<textarea\b[^>]*>")

        If Not textareaStartTagMatch.Success Then
            Continue For
        End If

        If IsDisabled(textareaStartTagMatch.Value) Then
            Continue For
        End If

        Dim textareaName As String = GetAttribute(textareaStartTagMatch.Value, "name")

        If String.IsNullOrWhiteSpace(textareaName) Then
            Continue For
        End If

        Dim textareaValueMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                textareaHtml,
                "(?is)<textarea\b[^>]*>" &
                "(?<value>.*?)" &
                "</textarea>"
            )

        Dim textareaValue As String = ""

        If textareaValueMatch.Success Then
            textareaValue = System.Net.WebUtility.HtmlDecode(textareaValueMatch.Groups("value").Value)
        End If

        indexedFields.Add(
            Tuple.Create(
                textareaMatch.Index,
                New System.Collections.Generic.KeyValuePair(Of String, String)(textareaName, textareaValue)
            )
        )

    Next


    ' ============================================================
    ' SORT FIELDS
    ' ============================================================

    indexedFields.Sort(
        Function(
            leftItem As Tuple(Of Integer, System.Collections.Generic.KeyValuePair(Of String, String)),
            rightItem As Tuple(Of Integer, System.Collections.Generic.KeyValuePair(Of String, String))
        ) As Integer
            Return leftItem.Item1.CompareTo(rightItem.Item1)
        End Function
    )

    Dim formData As New List(Of System.Collections.Generic.KeyValuePair(Of String, String))

    For Each indexedField As Tuple(Of Integer, System.Collections.Generic.KeyValuePair(Of String, String)) In indexedFields
        formData.Add(indexedField.Item2)
    Next

    Console.WriteLine("Extracted form fields: " & formData.Count.ToString())


    ' ============================================================
    ' FORM HELPERS
    ' ============================================================

    Dim FieldExists As Func(Of String, Boolean) =
        Function(fieldName As String) As Boolean

            For Each item As System.Collections.Generic.KeyValuePair(Of String, String) In formData
                If String.Equals(item.Key, fieldName, StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next

            Return False

        End Function


    Dim GetField As Func(Of String, String) =
        Function(fieldName As String) As String

            For Each item As System.Collections.Generic.KeyValuePair(Of String, String) In formData
                If String.Equals(item.Key, fieldName, StringComparison.OrdinalIgnoreCase) Then
                    Return If(item.Value, "")
                End If
            Next

            Return ""

        End Function


    Dim SetField As Action(Of String, String, Boolean) =
        Sub(fieldName As String, fieldValue As String, addIfMissing As Boolean)

            Dim fieldWasFound As Boolean = False

            For fieldIndex As Integer = 0 To formData.Count - 1

                If String.Equals(formData(fieldIndex).Key, fieldName, StringComparison.OrdinalIgnoreCase) Then

                    formData(fieldIndex) =
                        New System.Collections.Generic.KeyValuePair(Of String, String)(
                            formData(fieldIndex).Key,
                            If(fieldValue, "")
                        )

                    fieldWasFound = True

                End If

            Next

            If Not fieldWasFound AndAlso addIfMissing Then
                formData.Add(
                    New System.Collections.Generic.KeyValuePair(Of String, String)(
                        fieldName,
                        If(fieldValue, "")
                    )
                )
            End If

        End Sub


    Dim RemoveField As Action(Of String) =
        Sub(fieldName As String)

            For i As Integer = formData.Count - 1 To 0 Step -1
                If String.Equals(formData(i).Key, fieldName, StringComparison.OrdinalIgnoreCase) Then
                    formData.RemoveAt(i)
                End If
            Next

        End Sub


    ' ============================================================
    ' VALIDATE REQUIRED PAGE FIELDS
    ' ============================================================

    If Not FieldExists("__VIEWSTATE") Then
        Throw New Exception("__VIEWSTATE was not found in the Sandata page.")
    End If

    If Not FieldExists("ctl00$cphContent$ddlStatus") Then
        Throw New Exception("The Sandata status field was not found.")
    End If


    ' ============================================================
    ' UPDATE PROPOSED VISIT FIELDS
    '
    ' Do not set __EVENTTARGET or btnSave here.
    ' This code prepares values only.
    ' ============================================================

    SetField("ctl00$cphContent$ddlStatus", statusValue, False)
    SetField("ctl00$cphContent$txtComment", commentValue, True)
    SetField("ctl00$cphContent$hdnEditReasonCode", reasonCodeValue, True)
    SetField("ctl00$cphContent$hdnOMIGReasonCode", reasonCodeValue, True)
    SetField("ctl00$cphContent$hdnOMIGResoCode", "12", True)


    ' ============================================================
    ' GPS DISTANCE LOGIC
    ' ============================================================

    If exceptionCategoryValue.IndexOf("gps distance", StringComparison.OrdinalIgnoreCase) >= 0 Then

        Dim phoneIconMatches As System.Text.RegularExpressions.MatchCollection =
            System.Text.RegularExpressions.Regex.Matches(
                pageHtml,
                "(?is)<img[^>]+src\s*=\s*[""']" &
                "[^""']*" &
                "(iconCellphoneException|" &
                "iconCellphoneNoException|" &
                "iconCellphoneUnknownLoc)" &
                "\.png[^""']*[""'][^>]*>"
            )

        Console.WriteLine("Phone icons found: " & phoneIconMatches.Count.ToString())

        If phoneIconMatches.Count >= 1 Then

            Dim callInIcon As String = phoneIconMatches(0).Groups(1).Value

            If callInIcon.Equals("iconCellphoneException", StringComparison.OrdinalIgnoreCase) Then
                SetField("ctl00$cphContent$ddlCallInLoc", "C", False)
            ElseIf callInIcon.Equals("iconCellphoneUnknownLoc", StringComparison.OrdinalIgnoreCase) Then
                SetField("ctl00$cphContent$ddlCallInLoc", "H", False)
            End If

        End If

        If phoneIconMatches.Count >= 2 Then

            Dim callOutIcon As String = phoneIconMatches(1).Groups(1).Value

            If callOutIcon.Equals("iconCellphoneException", StringComparison.OrdinalIgnoreCase) Then
                SetField("ctl00$cphContent$ddlCallOutLoc", "C", False)
            ElseIf callOutIcon.Equals("iconCellphoneUnknownLoc", StringComparison.OrdinalIgnoreCase) Then
                SetField("ctl00$cphContent$ddlCallOutLoc", "H", False)
            End If

        End If

    End If


    ' ============================================================
    ' RESTORE HOURS
    ' ============================================================

    Dim proposedHoursValue As String = GetField("ctl00$cphContent$hdnProposedHrs").Trim()
    Dim actualHoursValue As String = GetField("ctl00$cphContent$txtActualHrs").Trim()

    If String.IsNullOrWhiteSpace(actualHoursValue) Then
        actualHoursValue = GetField("ctl00$cphContent$hdnActualHrs").Trim()
    End If

    If String.IsNullOrWhiteSpace(actualHoursValue) Then
        actualHoursValue = proposedHoursValue
    End If

    Dim adjustedHoursValue As String = GetField("ctl00$cphContent$hdnAdjustedHrs").Trim()

    If String.IsNullOrWhiteSpace(adjustedHoursValue) Then
        adjustedHoursValue = actualHoursValue
    End If

    If String.IsNullOrWhiteSpace(proposedHoursValue) Then
        proposedHoursValue = actualHoursValue
    End If

    If String.IsNullOrWhiteSpace(actualHoursValue) Then
        Throw New Exception("Actual hours could not be extracted.")
    End If

    SetField("ctl00$cphContent$txtActualHrs", actualHoursValue, False)
    SetField("ctl00$cphContent$hdnProposedHrs", proposedHoursValue, False)
    SetField("ctl00$cphContent$hdnProposedHrs1", proposedHoursValue, False)
    SetField("ctl00$cphContent$hdnActualHrs", actualHoursValue, False)
    SetField("ctl00$cphContent$hdnActualHrs1", actualHoursValue, False)
    SetField("ctl00$cphContent$hdnAdjustedHrs", adjustedHoursValue, False)
    SetField("ctl00$cphContent$hdnAdjustedHrs1", adjustedHoursValue, False)


    ' ============================================================
    ' RESTORE ACTUAL AND ADJUSTED TIMES
    ' ============================================================

    Dim actualInValue As String = GetField("ctl00$cphContent$txtActualIn").Trim()
    Dim actualOutValue As String = GetField("ctl00$cphContent$txtActualOut").Trim()

    If String.IsNullOrWhiteSpace(actualInValue) Then
        actualInValue = GetField("ctl00$cphContent$hdnActualIn").Trim()
    End If

    If String.IsNullOrWhiteSpace(actualOutValue) Then
        actualOutValue = GetField("ctl00$cphContent$hdnActualOut").Trim()
    End If

    Dim adjustedInValue As String = GetField("ctl00$cphContent$txtAdjustedIn").Trim()
    Dim adjustedOutValue As String = GetField("ctl00$cphContent$txtAdjustedOut").Trim()

    If String.IsNullOrWhiteSpace(adjustedInValue) Then
        adjustedInValue = actualInValue
    End If

    If String.IsNullOrWhiteSpace(adjustedOutValue) Then
        adjustedOutValue = actualOutValue
    End If

    SetField("ctl00$cphContent$hdnActualIn", actualInValue, True)
    SetField("ctl00$cphContent$hdnActualOut", actualOutValue, True)
    SetField("ctl00$cphContent$hdnAdjustedIn", adjustedInValue, True)
    SetField("ctl00$cphContent$hdnAdjustedOut", adjustedOutValue, True)

    RemoveField("ctl00$cphContent$txtAdjustedHrs")
    RemoveField("ctl00$cphContent$txtTTEstimate")


    ' ============================================================
    ' SET JAVASCRIPT-GENERATED GENERAL FIELDS
    ' ============================================================

    Dim selectedClientValue As String = GetField("ctl00$cphContent$ddlAddedClient").Trim()
    Dim selectedStaffValue As String = GetField("ctl00$cphContent$ddlAddedStaff").Trim()
    Dim serviceValue As String = GetField("ctl00$cphContent$ddlService").Trim()
    Dim eventCodeValue As String = GetField("ctl00$cphContent$ddlEventCodesAll").Trim()

    If String.IsNullOrWhiteSpace(eventCodeValue) Then
        eventCodeValue = "DEF"
    End If

    SetField("ctl00$cphContent$hdnSelectedStaff", selectedStaffValue, True)
    SetField("ctl00$cphContent$hdnService", serviceValue, True)
    SetField("ctl00$cphContent$hdnEventCodes", eventCodeValue, True)
    SetField("ctl00$cphContent$hdnStatus", statusValue, True)


    ' ============================================================
    ' RECONSTRUCT CLIENT ATTRIBUTES
    ' ============================================================

    Dim clientJson As String = GetJavascriptString("clientJSON")

    If String.IsNullOrWhiteSpace(clientJson) Then
        Throw New Exception("clientJSON was not found in the Sandata page.")
    End If

    Dim clientSceId As String = GetJsonValue(clientJson, "sceID")
    Dim clientId As String = GetJsonValue(clientJson, "clientID")
    Dim clientName As String = GetJsonValue(clientJson, "clientName")
    Dim clientBillAs As String = GetJsonValue(clientJson, "billAs")
    Dim clientOverride As String = GetJsonValue(clientJson, "override")
    Dim clientTelephony As String = GetJsonValue(clientJson, "telephony")
    Dim clientTimezone As String = GetJsonValue(clientJson, "timezone")

    If String.IsNullOrWhiteSpace(clientTimezone) Then
        clientTimezone = GetField("ctl00$cphContent$hdnTimeZone").Trim()
    End If

    Dim clientSedId As String = GetJsonValue(clientJson, "sedID")
    Dim clientEvent As String = GetJsonValue(clientJson, "event")
    Dim clientType As String = GetJsonValue(clientJson, "type")
    Dim clientQty As String = GetJsonValue(clientJson, "qty")
    Dim clientRate As String = FormatMoney(GetJsonValue(clientJson, "rate"))
    Dim clientBillAmount As String = FormatMoney(GetJsonValue(clientJson, "billAmt"))
    Dim clientStatus As String = GetJsonValue(clientJson, "status")
    Dim clientCopay As String = FormatMoney(GetJsonValue(clientJson, "copay"))
    Dim clientShiftRateId As String = GetJsonValue(clientJson, "ShiftRateID")
    Dim clientShiftBegin As String = GetJsonValue(clientJson, "ShiftBeginTime")
    Dim clientShiftEnd As String = GetJsonValue(clientJson, "ShiftEndTime")
    Dim clientFlag As String = GetJsonValue(clientJson, "flag")

    Dim clientAttrValue As String =
        clientSceId & "#" &
        clientId & "#" &
        clientName & "#" &
        clientBillAs & "#" &
        clientOverride & "#" &
        clientTelephony & "#" &
        clientTimezone & "#" &
        clientSedId & "@" &
        clientEvent & "@" &
        clientType & "@" &
        clientQty & "@" &
        clientRate & "@" &
        clientBillAmount & "@" &
        clientStatus & "@" &
        clientCopay & "@" &
        clientShiftRateId & "@" &
        clientShiftBegin & "@" &
        clientShiftEnd & "@" &
        clientFlag & "#-1"

    If String.IsNullOrWhiteSpace(clientId) OrElse String.IsNullOrWhiteSpace(clientSedId) Then
        Throw New Exception("Unable to reconstruct Sandata client attributes.")
    End If

    SetField("ctl00$cphContent$hdnClientsAttr", clientAttrValue, True)


    ' ============================================================
    ' RECONSTRUCT STAFF ATTRIBUTES
    ' ============================================================

    Dim staffJson As String = GetJavascriptString("staffJSON")

    If String.IsNullOrWhiteSpace(staffJson) Then
        Throw New Exception("staffJSON was not found in the Sandata page.")
    End If

    Dim staffSceId As String = GetJsonValue(staffJson, "sceID")
    Dim staffId As String = GetJsonValue(staffJson, "staffID")
    Dim staffName As String = GetJsonValue(staffJson, "staffName")
    Dim staffPayAs As String = GetJsonValue(staffJson, "payAs")
    Dim staffOverride As String = GetJsonValue(staffJson, "override")
    Dim staffTelephony As String = GetJsonValue(staffJson, "telephony")
    Dim staffTimezone As String = GetJsonValue(staffJson, "timezone")

    If String.IsNullOrWhiteSpace(staffTimezone) Then
        staffTimezone = GetField("ctl00$cphContent$hdnTimeZone").Trim()
    End If

    Dim staffSedId As String = GetJsonValue(staffJson, "sedID")
    Dim staffEvent As String = GetJsonValue(staffJson, "event")
    Dim staffType As String = GetJsonValue(staffJson, "type")
    Dim staffQty As String = GetJsonValue(staffJson, "qty")
    Dim staffRate As String = FormatMoney(GetJsonValue(staffJson, "rate"))
    Dim staffPayAmount As String = FormatMoney(GetJsonValue(staffJson, "payAmt"))
    Dim staffStatus As String = GetJsonValue(staffJson, "status")
    Dim staffFlag As String = GetJsonValue(staffJson, "flag")
    Dim staffShiftRateId As String = GetJsonValue(staffJson, "ShiftRateID")
    Dim staffShiftBegin As String = GetJsonValue(staffJson, "ShiftBeginTime")
    Dim staffShiftEnd As String = GetJsonValue(staffJson, "ShiftEndTime")

    Dim staffAttrValue As String =
        staffSceId & "#" &
        staffId & "#" &
        staffName & "#" &
        staffPayAs & "#" &
        staffOverride & "#" &
        staffTelephony & "#" &
        staffTimezone & "#" &
        staffSedId & "@" &
        staffEvent & "@" &
        staffType & "@" &
        staffQty & "@" &
        staffRate & "@" &
        staffPayAmount & "@" &
        staffStatus & "@" &
        staffFlag & "@" &
        staffShiftRateId & "@" &
        staffShiftBegin & "@" &
        staffShiftEnd

    If String.IsNullOrWhiteSpace(staffId) OrElse String.IsNullOrWhiteSpace(staffSedId) Then
        Throw New Exception("Unable to reconstruct Sandata staff attributes.")
    End If

    SetField("ctl00$cphContent$hdnStaffsAttr", staffAttrValue, True)


    ' ============================================================
    ' REMOVE ALL SAVE TRIGGERS
    ' ============================================================

    RemoveField("__EVENTTARGET")
    RemoveField("__EVENTARGUMENT")
    RemoveField("ctl00$cphContent$btnSave")


    ' ============================================================
    ' LOG IMPORTANT VALUES
    ' ============================================================

    Console.WriteLine("Status=[" & statusValue & "]")
    Console.WriteLine("ActualIn=[" & actualInValue & "]")
    Console.WriteLine("ActualOut=[" & actualOutValue & "]")
    Console.WriteLine("ActualHours=[" & actualHoursValue & "]")
    Console.WriteLine("SelectedClient=[" & selectedClientValue & "]")
    Console.WriteLine("SelectedStaff=[" & selectedStaffValue & "]")
    Console.WriteLine("Service=[" & serviceValue & "]")
    Console.WriteLine("EventCode=[" & eventCodeValue & "]")
    Console.WriteLine("ClientAttr=[" & clientAttrValue & "]")
    Console.WriteLine("StaffAttr=[" & staffAttrValue & "]")


    ' ============================================================
    ' RETURN PREPARED FORM DATA
    '
    ' No POST is made by this Invoke Code.
    ' Nothing is saved to Sandata.
    ' ============================================================

    updatedFormData = New List(Of System.Collections.Generic.KeyValuePair(Of String, String))(formData)

    prepareSuccessful = True
    errorMessage = ""

    Console.WriteLine("Sandata form data prepared successfully.")
    Console.WriteLine("Schedule ID: " & scheduleID)
    Console.WriteLine("Prepared field count: " & updatedFormData.Count.ToString())


Catch webException As System.Net.WebException

    prepareSuccessful = False
    updatedFormData = New List(Of System.Collections.Generic.KeyValuePair(Of String, String))()

    Dim webResponseText As String = ""

    If webException.Response IsNot Nothing Then

        Try

            Using errorResponse As System.Net.HttpWebResponse =
                CType(webException.Response, System.Net.HttpWebResponse)

                Using errorReader As New System.IO.StreamReader(errorResponse.GetResponseStream())
                    webResponseText = errorReader.ReadToEnd()
                End Using

            End Using

        Catch
        End Try

    End If

    errorMessage = "Sandata form preparation failed: " & webException.ToString()

    If Not String.IsNullOrWhiteSpace(webResponseText) Then
        errorMessage &=
            Environment.NewLine &
            "Server response: " &
            webResponseText.Substring(0, Math.Min(2000, webResponseText.Length))
    End If

    Console.WriteLine(errorMessage)


Catch ex As Exception

    prepareSuccessful = False
    updatedFormData = New List(Of System.Collections.Generic.KeyValuePair(Of String, String))()

    errorMessage = "Sandata form preparation failed: " & ex.ToString()

    Console.WriteLine(errorMessage)

End Try
