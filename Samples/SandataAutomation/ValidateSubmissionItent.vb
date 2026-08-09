' ============================================================
' SANDATA VISIT PRE-SAVE VALIDATION
' ============================================================

validationPassed = False
validationTrace = ""
errorMessage = ""
warningList = New List(Of String)()

Dim trace As New System.Text.StringBuilder()

Try

    If String.IsNullOrWhiteSpace(in_ScheduleID) Then
        Throw New Exception("Schedule ID is required.")
    End If

    If in_Cookies Is Nothing Then
        Throw New Exception("Authenticated Sandata cookies are required.")
    End If

    Dim localCookies As System.Net.CookieContainer = in_Cookies
    Dim baseUrl As String = "https://us.sandata.com"
    Dim scheduleUrl As String =
        baseUrl & "/Scheduling/AddEditSchedule.aspx?schedID=" &
        System.Uri.EscapeDataString(in_ScheduleID.Trim())

    trace.AppendLine(
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") &
        " Starting Sandata validation for schedule " &
        in_ScheduleID.Trim()
    )

    ' ========================================================
    ' HTTP GET
    ' ========================================================

    Dim DoGet As Func(Of String, String, String) =
        Function(requestUrl As String, refererUrl As String) As String

            trace.AppendLine("GET " & requestUrl)

            Dim request As System.Net.HttpWebRequest =
                CType(System.Net.WebRequest.Create(requestUrl), System.Net.HttpWebRequest)

            request.Method = "GET"
            request.CookieContainer = localCookies
            request.AllowAutoRedirect = True
            request.KeepAlive = True
            request.Timeout = 120000
            request.ReadWriteTimeout = 120000

            request.AutomaticDecompression =
                System.Net.DecompressionMethods.GZip Or System.Net.DecompressionMethods.Deflate

            request.Accept = "*/*"

            request.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
                "AppleWebKit/537.36 (KHTML, like Gecko) " &
                "Chrome/150.0.0.0 Safari/537.36"

            If Not String.IsNullOrWhiteSpace(refererUrl) Then
                request.Referer = refererUrl
            End If

            Using response As System.Net.HttpWebResponse =
                CType(request.GetResponse(), System.Net.HttpWebResponse)

                trace.AppendLine(
                    "HTTP " & CInt(response.StatusCode).ToString() & " " & response.StatusDescription
                )

                Dim finalUrl As String = response.ResponseUri.AbsoluteUri

                If finalUrl.IndexOf("/Security/Login", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Throw New Exception("Sandata session expired. Redirected to: " & finalUrl)
                End If

                Using reader As New System.IO.StreamReader(response.GetResponseStream())
                    Return reader.ReadToEnd()
                End Using

            End Using

        End Function

    ' ========================================================
    ' GET SCHEDULE PAGE
    ' ========================================================

    Dim pageHtml As String = DoGet(scheduleUrl, scheduleUrl)

    If String.IsNullOrWhiteSpace(pageHtml) Then
        Throw New Exception("Sandata returned an empty schedule page.")
    End If

    If pageHtml.IndexOf("__VIEWSTATE", StringComparison.OrdinalIgnoreCase) < 0 Then
        Throw New Exception("The Sandata schedule page did not contain __VIEWSTATE.")
    End If

    ' ========================================================
    ' READ FORM CONTROLS
    ' ========================================================

    Dim currentFormData As New List(Of System.Collections.Generic.KeyValuePair(Of String, String))

    Dim inputMatches As System.Text.RegularExpressions.MatchCollection =
        System.Text.RegularExpressions.Regex.Matches(pageHtml, "(?is)<input\b[^>]*>")

    For Each inputMatch As System.Text.RegularExpressions.Match In inputMatches

        Dim inputTag As String = inputMatch.Value

        Dim nameMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                inputTag,
                "(?is)\bname\s*=\s*[""'](?<value>.*?)[""']"
            )

        If Not nameMatch.Success Then
            Continue For
        End If

        Dim fieldName As String = System.Net.WebUtility.HtmlDecode(nameMatch.Groups("value").Value)

        Dim typeMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                inputTag,
                "(?is)\btype\s*=\s*[""'](?<value>.*?)[""']"
            )

        Dim inputType As String = "text"

        If typeMatch.Success Then
            inputType = typeMatch.Groups("value").Value.Trim().ToLowerInvariant()
        End If

        If inputType = "submit" OrElse
           inputType = "button" OrElse
           inputType = "image" OrElse
           inputType = "file" OrElse
           inputType = "reset" Then

            Continue For

        End If

        If inputType = "checkbox" OrElse inputType = "radio" Then

            If Not System.Text.RegularExpressions.Regex.IsMatch(inputTag, "(?is)\bchecked\b") Then
                Continue For
            End If

        End If

        Dim valueMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                inputTag,
                "(?is)\bvalue\s*=\s*[""'](?<value>.*?)[""']"
            )

        Dim fieldValue As String = ""

        If valueMatch.Success Then
            fieldValue = System.Net.WebUtility.HtmlDecode(valueMatch.Groups("value").Value)
        ElseIf inputType = "checkbox" OrElse inputType = "radio" Then
            fieldValue = "on"
        End If

        currentFormData.Add(
            New System.Collections.Generic.KeyValuePair(Of String, String)(fieldName, fieldValue)
        )

    Next

    Dim selectMatches As System.Text.RegularExpressions.MatchCollection =
        System.Text.RegularExpressions.Regex.Matches(
            pageHtml,
            "(?is)<select\b(?<attrs>[^>]*)>(?<options>.*?)</select>"
        )

    For Each selectMatch As System.Text.RegularExpressions.Match In selectMatches

        Dim attrs As String = selectMatch.Groups("attrs").Value

        Dim nameMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                attrs,
                "(?is)\bname\s*=\s*[""'](?<value>.*?)[""']"
            )

        If Not nameMatch.Success Then
            Continue For
        End If

        Dim fieldName As String = System.Net.WebUtility.HtmlDecode(nameMatch.Groups("value").Value)
        Dim optionsHtml As String = selectMatch.Groups("options").Value

        Dim optionMatches As System.Text.RegularExpressions.MatchCollection =
            System.Text.RegularExpressions.Regex.Matches(
                optionsHtml,
                "(?is)<option\b(?<attrs>[^>]*)>(?<text>.*?)</option>"
            )

        Dim selectedValue As String = ""
        Dim firstValue As String = ""
        Dim selectedFound As Boolean = False
        Dim firstValueFound As Boolean = False

        For Each optionMatch As System.Text.RegularExpressions.Match In optionMatches

            Dim optionAttrs As String = optionMatch.Groups("attrs").Value

            Dim valueMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    optionAttrs,
                    "(?is)\bvalue\s*=\s*[""'](?<value>.*?)[""']"
                )

            Dim optionValue As String = ""

            If valueMatch.Success Then
                optionValue = System.Net.WebUtility.HtmlDecode(valueMatch.Groups("value").Value)
            Else
                optionValue =
                    System.Text.RegularExpressions.Regex.Replace(
                        optionMatch.Groups("text").Value,
                        "<[^>]+>",
                        ""
                    )
                optionValue = System.Net.WebUtility.HtmlDecode(optionValue).Trim()
            End If

            If Not firstValueFound Then
                firstValue = optionValue
                firstValueFound = True
            End If

            If System.Text.RegularExpressions.Regex.IsMatch(optionAttrs, "(?is)\bselected\b") Then
                selectedValue = optionValue
                selectedFound = True
                Exit For
            End If

        Next

        If Not selectedFound Then
            selectedValue = firstValue
        End If

        currentFormData.Add(
            New System.Collections.Generic.KeyValuePair(Of String, String)(fieldName, selectedValue)
        )

    Next

    Dim textareaMatches As System.Text.RegularExpressions.MatchCollection =
        System.Text.RegularExpressions.Regex.Matches(
            pageHtml,
            "(?is)<textarea\b(?<attrs>[^>]*)>(?<value>.*?)</textarea>"
        )

    For Each textareaMatch As System.Text.RegularExpressions.Match In textareaMatches

        Dim attrs As String = textareaMatch.Groups("attrs").Value

        Dim nameMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                attrs,
                "(?is)\bname\s*=\s*[""'](?<value>.*?)[""']"
            )

        If Not nameMatch.Success Then
            Continue For
        End If

        currentFormData.Add(
            New System.Collections.Generic.KeyValuePair(Of String, String)(
                System.Net.WebUtility.HtmlDecode(nameMatch.Groups("value").Value),
                System.Net.WebUtility.HtmlDecode(textareaMatch.Groups("value").Value)
            )
        )

    Next

    ' ========================================================
    ' GET VALUE
    ' ========================================================

    Dim GetValue As Func(Of String(), String) =
        Function(possibleNames As String()) As String

            If possibleNames Is Nothing Then
                Return ""
            End If

            If in_UpdatedFormData IsNot Nothing Then

                For Each possibleName As String In possibleNames

                    For i As Integer = in_UpdatedFormData.Count - 1 To 0 Step -1

                        Dim item = in_UpdatedFormData(i)

                        If String.Equals(item.Key, possibleName, StringComparison.OrdinalIgnoreCase) OrElse
                           item.Key.EndsWith(possibleName, StringComparison.OrdinalIgnoreCase) Then

                            Return If(item.Value, "").Trim()

                        End If

                    Next

                Next

            End If

            For Each possibleName As String In possibleNames

                For i As Integer = currentFormData.Count - 1 To 0 Step -1

                    Dim item = currentFormData(i)

                    If String.Equals(item.Key, possibleName, StringComparison.OrdinalIgnoreCase) OrElse
                       item.Key.EndsWith(possibleName, StringComparison.OrdinalIgnoreCase) Then

                        Return If(item.Value, "").Trim()

                    End If

                Next

            Next

            Return ""

        End Function

    Dim GetSingleValue As Func(Of String, String) =
        Function(name As String) As String
            Return GetValue(New String() {name})
        End Function

    ' ========================================================
    ' EXTRACT RESULT
    ' ========================================================

    Dim ExtractResultValue As Func(Of String, String) =
        Function(html As String) As String

            If String.IsNullOrWhiteSpace(html) Then
                Return ""
            End If

            ' ====================================================
            ' 1. READ lblResult SPAN
            ' ====================================================

            Dim resultMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "(?is)<span\b[^>]*\bid\s*=\s*[""'][^""']*lblResult[""'][^>]*>(?<value>.*?)</span>"
                )

            If resultMatch.Success Then

                Dim spanValue As String =
                    System.Text.RegularExpressions.Regex.Replace(
                        resultMatch.Groups("value").Value,
                        "<[^>]+>",
                        ""
                    )

                spanValue = System.Net.WebUtility.HtmlDecode(spanValue).Trim()

                If Not String.IsNullOrWhiteSpace(spanValue) Then
                    Return spanValue
                End If

            End If

            ' ====================================================
            ' 2. READ txtResult INPUT VALUE
            ' ====================================================

            resultMatch =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "(?is)<input\b(?=[^>]*\bid\s*=\s*[""']txtResult[""'])[^>]*\bvalue\s*=\s*[""'](?<value>.*?)[""'][^>]*>"
                )

            If resultMatch.Success Then

                Dim inputValue As String =
                    System.Net.WebUtility.HtmlDecode(resultMatch.Groups("value").Value).Trim()

                If Not String.IsNullOrWhiteSpace(inputValue) Then
                    Return inputValue
                End If

            End If

            ' ====================================================
            ' 3. READ JAVASCRIPT RESULT
            ' ====================================================

            Dim patterns As String() = {
                "(?is)getElementById\([""']txtResult[""']\)\.value\s*=\s*[""'](?<value>.*?)[""']\s*;",
                "(?is)getElementById\([""']lblResult[""']\)\.innerHTML\s*=\s*[""'](?<value>.*?)[""']\s*;",
                "(?is)getElementById\([""']lblResult[""']\)\.innerText\s*=\s*[""'](?<value>.*?)[""']\s*;",
                "(?is)\btxtResult\.value\s*=\s*[""'](?<value>.*?)[""']\s*;",
                "(?is)\btxtResult\s*=\s*[""'](?<value>.*?)[""']\s*;"
            }

            For Each pattern As String In patterns

                resultMatch = System.Text.RegularExpressions.Regex.Match(html, pattern)

                If resultMatch.Success Then

                    Dim result As String = resultMatch.Groups("value").Value

                    result = result.Replace("\r\n", Environment.NewLine)
                    result = result.Replace("\n", Environment.NewLine)
                    result = result.Replace("\r", Environment.NewLine)
                    result = result.Replace("\'", "'")
                    result = result.Replace("\""", """")
                    result = result.Replace("\\", "\")
                    result = result.Replace("vbNewLine", Environment.NewLine)
                    result = System.Net.WebUtility.HtmlDecode(result)

                    Return result.Trim()

                End If

            Next

            Return ""

        End Function

    ' ========================================================
    ' WARNING HELPER
    ' ========================================================

    Dim localWarningList As List(Of String) = warningList

    Dim AddWarning As Action(Of String, String) =
        Sub(warningType As String, warningMessage As String)

            If String.IsNullOrWhiteSpace(warningMessage) Then
                Exit Sub
            End If

            Dim finalWarning As String = warningType.Trim() & ": " & warningMessage.Trim()

            If Not localWarningList.Any(
                Function(existingWarning As String) As Boolean
                    Return String.Equals(existingWarning, finalWarning, StringComparison.OrdinalIgnoreCase)
                End Function
            ) Then

                localWarningList.Add(finalWarning)

            End If

            trace.AppendLine("Warning captured: " & finalWarning)

        End Sub

    ' ========================================================
    ' QUERY HELPERS
    ' ========================================================

    Dim Encode As Func(Of String, String) =
        Function(value As String) As String
            Return System.Uri.EscapeDataString(If(value, ""))
        End Function

    Dim BuildQuery As Func(
        Of List(Of KeyValuePair(Of String, String)),
        List(Of String),
        String
    ) =
        Function(
            parameters As List(Of KeyValuePair(Of String, String)),
            valuelessParameters As List(Of String)
        ) As String

            Dim parts As New List(Of String)

            For Each item As KeyValuePair(Of String, String) In parameters
                parts.Add(Encode(item.Key) & "=" & Encode(item.Value))
            Next

            If valuelessParameters IsNot Nothing Then

                For Each parameterName As String In valuelessParameters
                    If Not String.IsNullOrWhiteSpace(parameterName) Then
                        parts.Add(Encode(parameterName))
                    End If
                Next

            End If

            Return String.Join("&", parts.ToArray())

        End Function

    ' ========================================================
    ' READ VALUES
    ' ========================================================

    Dim schedDate As String =
        GetValue(New String() {"txtScheduleDate", "txtDate", "hdnScheduleDate", "hdnSchedDate"})

    Dim schedDateOriginal As String =
        GetValue(New String() {"hdnDateOriginal", "hdnScheduleDateOriginal", "txtScheduleDate", "txtDate"})

    If String.IsNullOrWhiteSpace(schedDateOriginal) Then
        schedDateOriginal = schedDate
    End If

    Dim parsedDate As DateTime

    If DateTime.TryParse(
        schedDate,
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None,
        parsedDate
    ) Then

        schedDate = parsedDate.ToString("MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture)

    End If

    Dim schedServiceID As String = GetValue(New String() {"ddlService", "hdnService"})
    Dim schedStatusID As String = GetValue(New String() {"ddlStatus", "hdnStatus"})
    Dim schedCancelID As String = GetValue(New String() {"ddlCancelReason", "hdnCancelID"})

    If String.IsNullOrWhiteSpace(schedCancelID) Then
        schedCancelID = "-1"
    End If

    Dim schedCompanyID As String = GetValue(New String() {"hdnCompanyID", "ddlCompany"})
    Dim schedTimeIn As String = GetValue(New String() {"txtActualIn", "hdnActualIn"})
    Dim schedTimeOut As String = GetValue(New String() {"txtActualOut", "hdnActualOut"})

    Dim schedTimeInP As String =
        GetValue(New String() {"txtTimeIn", "txtProposedIn", "hdnProposedIn"})

    Dim schedTimeOutP As String =
        GetValue(New String() {"txtTimeOut", "txtProposedOut", "hdnProposedOut"})

    If String.IsNullOrWhiteSpace(schedTimeInP) Then
        schedTimeInP = schedTimeIn
    End If

    If String.IsNullOrWhiteSpace(schedTimeOutP) Then
        schedTimeOutP = schedTimeOut
    End If

    Dim schedTimeInS As String = schedDate & " " & schedTimeIn
    Dim schedTimeOutS As String = schedDate & " " & schedTimeOut

    Dim schedDuration As String =
        GetValue(New String() {"hdnAdjustedHrs", "hdnActualHrs", "txtActualHrs"})

    Dim schedDurationP As String = GetValue(New String() {"hdnProposedHrs", "txtProposedHrs"})
    Dim schedDurationS As String = GetValue(New String() {"hdnActualHrs", "txtActualHrs"})

    If String.IsNullOrWhiteSpace(schedDurationP) Then
        schedDurationP = schedDuration
    End If

    If String.IsNullOrWhiteSpace(schedDurationS) Then
        schedDurationS = schedDuration
    End If

    Dim eventCodes As String =
        GetValue(New String() {"hdnEventCodes", "ddlEventCode", "ddlEventCodesAll"})

    If Not String.IsNullOrWhiteSpace(eventCodes) AndAlso Not eventCodes.EndsWith(",") Then
        eventCodes &= ","
    End If

    Dim clientAttr As String = GetValue(New String() {"hdnClientsAttr"})
    Dim staffAttr As String = GetValue(New String() {"hdnStaffsAttr"})

    If String.IsNullOrWhiteSpace(clientAttr) Then
        Throw New Exception("Could not determine hdnClientsAttr for validation.")
    End If

    If String.IsNullOrWhiteSpace(staffAttr) Then
        Throw New Exception("Could not determine hdnStaffsAttr for validation.")
    End If

    Dim clientParts As String() = clientAttr.Split("#"c)
    Dim staffParts As String() = staffAttr.Split("#"c)

    Dim adID As String = ""
    Dim staffID As String = ""

    If clientParts.Length > 1 Then
        adID = clientParts(1).Trim()
    End If

    If staffParts.Length > 1 Then
        staffID = staffParts(1).Trim()
    ElseIf staffParts.Length > 0 Then
        staffID = staffParts(0).Trim()
    End If

    If String.IsNullOrWhiteSpace(adID) Then
        Throw New Exception("Could not determine the client adID.")
    End If

    If String.IsNullOrWhiteSpace(staffID) Then
        Throw New Exception("Could not determine the staff ID.")
    End If

    Dim clientParticipant As String = clientAttr.Replace("#", "*")
    Dim staffParticipant As String = staffAttr.Replace("#", "*")

    If clientParticipant.EndsWith("*-1", StringComparison.OrdinalIgnoreCase) Then
        clientParticipant &= "@^@^*"
    ElseIf Not clientParticipant.Contains("@^@^*") Then
        clientParticipant &= "*-1@^@^*"
    End If

    Dim participantItems As String = clientParticipant & staffParticipant
    Dim timestamp As String = "33"

    Dim schedWarnings As String = GetValue(New String() {"hdnWarnings", "hdnSchedWarnings"})

    If String.IsNullOrWhiteSpace(schedWarnings) Then
        schedWarnings = "0"
    End If

    Dim schedFlags As String = GetValue(New String() {"hdnFlags", "hdnSchedFlags"})

    If String.IsNullOrWhiteSpace(schedFlags) Then
        schedFlags = "0"
    End If

    Dim timezone As String = GetValue(New String() {"hdnTimeZone", "hdnTimezone"})

    If String.IsNullOrWhiteSpace(timezone) Then
        timezone = "-6:00"
    End If

    Dim billAs As String = GetValue(New String() {"ddlBillAs", "hdnBillAs"})

    If String.IsNullOrWhiteSpace(billAs) Then
        billAs = "01"
    End If

    Dim comments As String = GetValue(New String() {"txtComment", "txtComments"})
    Dim shiftGroupID As String = GetValue(New String() {"hdnShiftGroupID"})

    Dim clientLocationID As String =
        GetValue(New String() {"hdnClLocationID", "hdnClientLocationID"})

    Dim callInLocation As String = GetValue(New String() {"ddlCallInLoc"})
    Dim callOutLocation As String = GetValue(New String() {"ddlCallOutLoc"})

    If String.IsNullOrWhiteSpace(callInLocation) Then
        callInLocation = "-1"
    End If

    If String.IsNullOrWhiteSpace(callOutLocation) Then
        callOutLocation = "-1"
    End If

    Dim serviceName As String = GetValue(New String() {"hdnServiceName"})

    If String.IsNullOrWhiteSpace(serviceName) Then
        serviceName = schedServiceID
    End If

    ' ========================================================
    ' CHECK PROPOSED TIME
    ' ========================================================

    Dim proposedParameters As New List(Of KeyValuePair(Of String, String))

    proposedParameters.Add(New KeyValuePair(Of String, String)("schedDate", schedDate))
    proposedParameters.Add(New KeyValuePair(Of String, String)("adID", adID))
    proposedParameters.Add(New KeyValuePair(Of String, String)("type", "CheckProposedTimeForAct"))
    proposedParameters.Add(New KeyValuePair(Of String, String)("timeStamp", timestamp))

    Dim proposedUrl As String =
        baseUrl & "/Scheduling/AjaxRequestData.aspx?" & BuildQuery(proposedParameters, Nothing)

    Dim proposedHtml As String = DoGet(proposedUrl, scheduleUrl)
    Dim proposedResult As String = ExtractResultValue(proposedHtml)

    trace.AppendLine("CheckProposedTimeForAct response length: " & proposedHtml.Length.ToString())
    trace.AppendLine("CheckProposedTimeForAct result: [" & proposedResult & "]")

    If String.IsNullOrWhiteSpace(proposedResult) Then
        Throw New Exception(
            "CheckProposedTimeForAct returned no lblResult. Response: " &
            proposedHtml.Substring(0, Math.Min(1000, proposedHtml.Length))
        )
    End If

    If Not String.Equals(proposedResult.Trim(), "0", StringComparison.OrdinalIgnoreCase) Then
        AddWarning("CheckProposedTimeForAct", proposedResult)
    End If

    ' ========================================================
    ' VALIDATE AVP
    ' ========================================================

    Dim avpParameters As New List(Of KeyValuePair(Of String, String))

    Dim AddAvp As Action(Of String, String) =
        Sub(key As String, value As String)
            avpParameters.Add(New KeyValuePair(Of String, String)(key, If(value, "")))
        End Sub

    AddAvp("Type", "ValidateAvp")
    AddAvp("schedDate", schedDate)
    AddAvp("schedDateOriginal", schedDateOriginal)
    AddAvp("schedServiceID", schedServiceID)
    AddAvp("schedStatusID", schedStatusID)
    AddAvp("schedTimeIn", schedTimeIn)
    AddAvp("schedTimeInP", schedTimeInP)
    AddAvp("schedTimeInS", schedTimeIn)
    AddAvp("schedTimeOut", schedTimeOut)
    AddAvp("schedTimeOutP", schedTimeOutP)
    AddAvp("schedTimeOutS", schedTimeOut)
    AddAvp("schedDuration", schedDuration)
    AddAvp("schedEvents", eventCodes)
    AddAvp("schedWarnings", schedWarnings)
    AddAvp("BillAs", billAs)
    AddAvp("Staff", staffID & ",")
    AddAvp("Clients", adID & ",")
    AddAvp("schedTimeZone", timezone)
    AddAvp("schedComments", comments)
    AddAvp("schedLateMissedReason", GetSingleValue("hdnLateMissedReason"))
    AddAvp("schedStatusID2", "")
    AddAvp("schedID", in_ScheduleID.Trim())

    Dim avpValueless As New List(Of String)

    If Not String.IsNullOrWhiteSpace(shiftGroupID) Then
        avpValueless.Add("schedShiftGroupID" & shiftGroupID)
    End If

    Dim avpUrl As String =
        baseUrl & "/Scheduling/AjaxRequestData.aspx?" & BuildQuery(avpParameters, avpValueless)

    Dim avpHtml As String = DoGet(avpUrl, scheduleUrl)
    Dim avpResult As String = ExtractResultValue(avpHtml)

    trace.AppendLine("ValidateAvp response length: " & avpHtml.Length.ToString())
    trace.AppendLine("ValidateAvp result: [" & avpResult & "]")

    If String.IsNullOrWhiteSpace(avpResult) Then
        Throw New Exception(
            "ValidateAvp returned no lblResult. Response: " &
            avpHtml.Substring(0, Math.Min(1000, avpHtml.Length))
        )
    End If

    If Not String.Equals(avpResult.Trim(), "0:0:0", StringComparison.OrdinalIgnoreCase) Then
        AddWarning("ValidateAvp", avpResult)
    End If

    ' ========================================================
    ' VALIDATE SCHEDULE
    ' ========================================================

    Dim scheduleParameters As New List(Of KeyValuePair(Of String, String))

    Dim AddSchedule As Action(Of String, String) =
        Sub(key As String, value As String)
            scheduleParameters.Add(New KeyValuePair(Of String, String)(key, If(value, "")))
        End Sub

    AddSchedule("schedDate", schedDate)
    AddSchedule("schedServiceID", schedServiceID)
    AddSchedule("schedID", in_ScheduleID.Trim())
    AddSchedule("schedCancelID", schedCancelID)
    AddSchedule("schedCompanyID", schedCompanyID)
    AddSchedule("schedStatusID", schedStatusID)
    AddSchedule("schedTimeIn", schedTimeIn)
    AddSchedule("schedTimeInP", schedTimeInP)
    AddSchedule("schedTimeInS", schedTimeIn)
    AddSchedule("schedTimeOut", schedTimeOut)
    AddSchedule("schedTimeOutP", schedTimeOutP)
    AddSchedule("schedTimeOutS", schedTimeOut)
    AddSchedule("schedTelephony", "0")
    AddSchedule("Type", "ValidateSched")
    AddSchedule("sec", timestamp)
    AddSchedule("participantItems", participantItems)
    AddSchedule("schedEvents", eventCodes)
    AddSchedule("recordMode", "Edit")
    AddSchedule("ServiceName", serviceName)
    AddSchedule("schedWarnings", schedWarnings)
    AddSchedule("schedFlags", schedFlags)
    AddSchedule("schedDurationP", schedDurationP)
    AddSchedule("schedDurationS", schedDurationS)
    AddSchedule("schedDuration", schedDuration)
    AddSchedule("schedLateMissedReason", GetSingleValue("hdnLateMissedReason"))
    AddSchedule("schedMakeupReason", GetSingleValue("hdnMakeupReason"))
    AddSchedule("schedMakeupID", GetSingleValue("hdnMakeupID"))
    AddSchedule("schedDateOriginal", schedDateOriginal)
    AddSchedule("schedTimezone", timezone)
    AddSchedule("schedOTABSQty", GetSingleValue("hdnOTABSQty"))
    AddSchedule("schedShiftGroupID", shiftGroupID)
    AddSchedule("schedClLocationID", clientLocationID)
    AddSchedule("schedCheckStaffRepl", "false")
    AddSchedule("schedOTSCReason", GetSingleValue("hdnOTSCReason"))
    AddSchedule("schedSplitShiftParentID", GetSingleValue("hdnSplitShiftParentID"))
    AddSchedule("GroupGUID", GetSingleValue("hdnGroupGUID"))
    AddSchedule("schedCallInLocation", callInLocation)
    AddSchedule("schedCallOutLocation", callOutLocation)

    Dim scheduleValueless As New List(Of String)
    scheduleValueless.Add("time" & timestamp)

    Dim validateScheduleUrl As String =
        baseUrl & "/Scheduling/AjaxRequestData.aspx?" & BuildQuery(scheduleParameters, scheduleValueless)

    Dim validationHtml As String = DoGet(validateScheduleUrl, scheduleUrl)
    Dim validationResult As String = ExtractResultValue(validationHtml)

    trace.AppendLine("ValidateSched response length: " & validationHtml.Length.ToString())
    trace.AppendLine("ValidateSched result: [" & validationResult & "]")

    If String.IsNullOrWhiteSpace(validationResult) Then

        If validationHtml.IndexOf("id=""lblResult""", StringComparison.OrdinalIgnoreCase) < 0 Then
            Throw New Exception(
                "ValidateSched returned an unexpected response: " &
                validationHtml.Substring(0, Math.Min(1500, validationHtml.Length))
            )
        End If

        validationResult = "0"
        trace.AppendLine("ValidateSched returned an empty lblResult. Treated as no warning.")

    End If

    Dim resultParts As String() =
        validationResult.Split(New String() {"~@~"}, StringSplitOptions.None)

    Dim validationMessage As String = resultParts(0).Trim()
    Dim validationType As String = "ValidateSched"

    If resultParts.Length > 1 AndAlso Not String.IsNullOrWhiteSpace(resultParts(1)) Then
        validationType = resultParts(1).Trim()
    End If

    If Not String.IsNullOrWhiteSpace(validationMessage) AndAlso
       Not String.Equals(validationMessage, "0", StringComparison.OrdinalIgnoreCase) AndAlso
       Not String.Equals(validationMessage, "0:0:0", StringComparison.OrdinalIgnoreCase) Then

        AddWarning(validationType, validationMessage)

    End If

    validationPassed = True
    errorMessage = ""

    trace.AppendLine("Validation requests completed successfully.")
    trace.AppendLine("Warnings found: " & warningList.Count.ToString())

    validationTrace = trace.ToString()

Catch ex As System.Net.WebException

    validationPassed = False

    If warningList Is Nothing Then
        warningList = New List(Of String)()
    End If

    Dim responseBody As String = ""

    If ex.Response IsNot Nothing Then

        Try

            Using errorResponse As System.Net.HttpWebResponse =
                CType(ex.Response, System.Net.HttpWebResponse)

                Using reader As New System.IO.StreamReader(errorResponse.GetResponseStream())
                    responseBody = reader.ReadToEnd()
                End Using

            End Using

        Catch
        End Try

    End If

    errorMessage = "Sandata validation request failed: " & ex.Message

    If Not String.IsNullOrWhiteSpace(responseBody) Then
        errorMessage &=
            Environment.NewLine &
            "Server response: " &
            responseBody.Substring(0, Math.Min(2000, responseBody.Length))
    End If

    trace.AppendLine(ex.ToString())
    validationTrace = trace.ToString()

Catch ex As Exception

    validationPassed = False

    If warningList Is Nothing Then
        warningList = New List(Of String)()
    End If

    errorMessage = "Sandata validation failed: " & ex.Message

    trace.AppendLine(ex.ToString())
    validationTrace = trace.ToString()

End Try
