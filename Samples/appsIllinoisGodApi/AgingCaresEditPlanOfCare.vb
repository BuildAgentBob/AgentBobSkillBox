' Opens EditProviderPlanOfCare from planOfCareId + assessmentId, accepts unsigned
' ProvidersServices cards with anticipatedStartDate and a PNG from signaturePngPath,
' syncs matching Services[i] signature/start fields, clears delay/decline, then
' POSTs Save & Close. cookies In/Out; errorMessage Out. Throws on failure.

Dim responseHtml As String = ""
Dim statusCode As Integer = 0
Dim finalUrl As String = ""
Dim serviceCount As Integer = 0
Dim success As Boolean = False

Try
    errorMessage = ""

    Console.WriteLine("=== AgingCares EditProviderPlanOfCare ===")

    If cookies Is Nothing Then
        Console.WriteLine("ABORT: cookies CookieContainer is Nothing — run AgingCaresLogin first.")
        Throw New System.Exception(
            "cookies CookieContainer is required."
        )
    End If

    ' Local copy — ByRef Invoke args cannot be captured by lambdas
    Dim cookieJar As System.Net.CookieContainer = cookies

    Dim pocId As String = If(planOfCareId, "").Trim()
    Dim assessId As String = If(assessmentId, "").Trim()
    Dim startDate As String = If(anticipatedStartDate, "").Trim()
    Dim pngPath As String = If(signaturePngPath, "").Trim()

    If String.IsNullOrWhiteSpace(pocId) Then
        Console.WriteLine("ABORT: planOfCareId is missing.")
        Throw New System.Exception("planOfCareId is required.")
    End If

    If String.IsNullOrWhiteSpace(assessId) Then
        Console.WriteLine("ABORT: assessmentId is missing.")
        Throw New System.Exception("assessmentId is required.")
    End If

    If String.IsNullOrWhiteSpace(startDate) Then
        Console.WriteLine("ABORT: anticipatedStartDate is missing.")
        Throw New System.Exception("anticipatedStartDate is required (mm/dd/yyyy).")
    End If

    If String.IsNullOrWhiteSpace(pngPath) Then
        Console.WriteLine("ABORT: signaturePngPath is missing.")
        Throw New System.Exception("signaturePngPath is required.")
    End If
    If Not System.IO.File.Exists(pngPath) Then
        Console.WriteLine("ABORT: signature PNG file not found.")
        Throw New System.Exception("signature PNG not found: " & pngPath)
    End If

    Dim bytes As Byte() = System.IO.File.ReadAllBytes(pngPath)
    Dim dataUrl As String =
        "data:image/png;base64," &
        System.Convert.ToBase64String(bytes)

    Console.WriteLine(
        "Signature PNG loaded | bytes=" & bytes.Length.ToString() &
        " | dataUrlChars=" & dataUrl.Length.ToString()
    )
    Console.WriteLine("Inputs OK | planOfCareId set | assessmentId set | startDateSet=True")

    System.Net.ServicePointManager.SecurityProtocol =
        System.Net.SecurityProtocolType.Tls12
    Try
        System.Net.ServicePointManager.SecurityProtocol =
            System.Net.SecurityProtocolType.Tls12 Or
            CType(12288, System.Net.SecurityProtocolType)
    Catch
    End Try
    System.Net.ServicePointManager.Expect100Continue = False

    Dim NormalizeUrl As Func(Of String, String) =
        Function(u As String) As String
            If String.IsNullOrWhiteSpace(u) Then
                Return ""
            End If
            Return u.Replace(":443/", "/").Replace(":443?", "?")
        End Function


    Dim GetAttr As Func(Of String, String, String) =
        Function(tag As String, attrName As String) As String
            If String.IsNullOrWhiteSpace(tag) Then
                Return ""
            End If
            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    tag,
                    attrName &
                    "\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )
            If Not m.Success Then
                Return ""
            End If
            If m.Groups(1).Success Then
                Return System.Net.WebUtility.HtmlDecode(m.Groups(1).Value)
            End If
            If m.Groups(2).Success Then
                Return System.Net.WebUtility.HtmlDecode(m.Groups(2).Value)
            End If
            If m.Groups(3).Success Then
                Return System.Net.WebUtility.HtmlDecode(m.Groups(3).Value)
            End If
            Return ""
        End Function


    Dim ExtractFormFields As Func(
        Of String,
        System.Collections.Generic.Dictionary(Of String, String)
    ) =
        Function(html As String)

            Dim result As New System.Collections.Generic.Dictionary(
                Of String, String
            )(StringComparer.OrdinalIgnoreCase)

            If String.IsNullOrWhiteSpace(html) Then
                Return result
            End If

            ' Prefer the EditProviderPlanOfCare form only
            Dim formHtml As String = html
            Dim formMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "<form\b[^>]*action\s*=\s*[""'][^""']*EditProviderPlanOfCare[^""']*[""'][^>]*>.*?</form>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )
            If formMatch.Success Then
                formHtml = formMatch.Value
            End If

            Dim inputMatches As System.Text.RegularExpressions.MatchCollection =
                System.Text.RegularExpressions.Regex.Matches(
                    formHtml,
                    "<input\b[^>]*>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            For Each m As System.Text.RegularExpressions.Match In inputMatches
                Dim tag As String = m.Value
                Dim name As String = GetAttr(tag, "name")
                If String.IsNullOrWhiteSpace(name) Then
                    Continue For
                End If

                Dim typ As String = GetAttr(tag, "type").ToLowerInvariant()
                If typ = "submit" OrElse typ = "button" OrElse typ = "image" Then
                    Continue For
                End If

                ' Skip unchecked checkboxes (except we'll keep companion hidden falses)
                If typ = "checkbox" Then
                    ' Only include if checked attribute present
                    If tag.IndexOf("checked", StringComparison.OrdinalIgnoreCase) < 0 Then
                        Continue For
                    End If
                End If

                ' Radios: last one in HTML wins unless we override later.
                ' Prefer checked radio when present.
                If typ = "radio" Then
                    If tag.IndexOf("checked", StringComparison.OrdinalIgnoreCase) < 0 Then
                        ' Keep existing if any; otherwise store as candidate
                        If result.ContainsKey(name) Then
                            Continue For
                        End If
                    End If
                End If

                result(name) = GetAttr(tag, "value")
            Next

            Dim taMatches As System.Text.RegularExpressions.MatchCollection =
                System.Text.RegularExpressions.Regex.Matches(
                    formHtml,
                    "<textarea\b[^>]*name\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>(.*?)</textarea>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            For Each m As System.Text.RegularExpressions.Match In taMatches
                Dim name As String = ""
                If m.Groups(1).Success Then
                    name = m.Groups(1).Value
                ElseIf m.Groups(2).Success Then
                    name = m.Groups(2).Value
                ElseIf m.Groups(3).Success Then
                    name = m.Groups(3).Value
                End If
                If Not String.IsNullOrWhiteSpace(name) Then
                    result(name) = System.Net.WebUtility.HtmlDecode(m.Groups(4).Value)
                End If
            Next

            Dim selectMatches As System.Text.RegularExpressions.MatchCollection =
                System.Text.RegularExpressions.Regex.Matches(
                    formHtml,
                    "<select\b[^>]*name\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>(.*?)</select>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            For Each m As System.Text.RegularExpressions.Match In selectMatches
                Dim name As String = ""
                If m.Groups(1).Success Then
                    name = m.Groups(1).Value
                ElseIf m.Groups(2).Success Then
                    name = m.Groups(2).Value
                ElseIf m.Groups(3).Success Then
                    name = m.Groups(3).Value
                End If
                If String.IsNullOrWhiteSpace(name) Then
                    Continue For
                End If

                Dim body As String = m.Groups(4).Value
                Dim selected As System.Text.RegularExpressions.Match =
                    System.Text.RegularExpressions.Regex.Match(
                        body,
                        "<option\b[^>]*selected\b[^>]*>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    )
                If Not selected.Success Then
                    selected =
                        System.Text.RegularExpressions.Regex.Match(
                            body,
                            "<option\b[^>]*>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        )
                End If
                If selected.Success Then
                    result(name) = GetAttr(selected.Value, "value")
                End If
            Next

            Return result
        End Function


    Dim BuildFormBody As Func(
        Of System.Collections.Generic.Dictionary(Of String, String),
        String
    ) =
        Function(formFields As System.Collections.Generic.Dictionary(Of String, String))
            Return String.Join(
                "&",
                formFields.Select(
                    Function(kvp)
                        Return System.Net.WebUtility.UrlEncode(kvp.Key) &
                               "=" &
                               System.Net.WebUtility.UrlEncode(If(kvp.Value, ""))
                    End Function
                )
            )
        End Function


    Dim SendRequest As Func(
        Of String,
        String,
        String,
        String,
        System.Tuple(Of String, String, Integer, String)
    ) =
        Function(
            method As String,
            url As String,
            body As String,
            referer As String
        )

            url = NormalizeUrl(url)

            Dim req As System.Net.HttpWebRequest =
                CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)

            req.Method = method
            req.CookieContainer = cookieJar
            req.AllowAutoRedirect = False
            req.KeepAlive = False
            req.Timeout = 180000
            req.ReadWriteTimeout = 180000

            req.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
                "AppleWebKit/537.36 (KHTML, like Gecko) " &
                "Chrome/153.0.0.0 Safari/537.36"

            req.Accept =
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"

            req.Headers(System.Net.HttpRequestHeader.AcceptLanguage) = "en-US,en;q=0.9"
            req.Headers(System.Net.HttpRequestHeader.CacheControl) = "no-cache"
            req.Headers(System.Net.HttpRequestHeader.Pragma) = "no-cache"

            req.AutomaticDecompression =
                System.Net.DecompressionMethods.GZip Or
                System.Net.DecompressionMethods.Deflate

            If Not String.IsNullOrWhiteSpace(referer) Then
                req.Referer = NormalizeUrl(referer)
            End If

            If method.Equals("POST", StringComparison.OrdinalIgnoreCase) Then
                Dim bodyBytes As Byte() =
                    System.Text.Encoding.UTF8.GetBytes(If(body, ""))
                req.ContentType = "application/x-www-form-urlencoded"
                req.ContentLength = bodyBytes.Length
                Try
                    req.Headers("Origin") = "https://webapps.illinois.gov"
                Catch
                End Try
                Using stream As System.IO.Stream = req.GetRequestStream()
                    stream.Write(bodyBytes, 0, bodyBytes.Length)
                End Using
            End If

            Dim resp As System.Net.HttpWebResponse = Nothing
            Try
                resp = CType(req.GetResponse(), System.Net.HttpWebResponse)
            Catch webEx As System.Net.WebException
                If webEx.Response Is Nothing Then
                    Console.WriteLine(method & " failed with no HTTP response: " & webEx.Message)
                    Throw
                End If
                Console.WriteLine(
                    method & " returned error status (will still read body): " & webEx.Message
                )
                resp = CType(webEx.Response, System.Net.HttpWebResponse)
            End Try

            Using response As System.Net.HttpWebResponse = resp
                Dim text As String = ""
                If response.GetResponseStream() IsNot Nothing Then
                    Using reader As New System.IO.StreamReader(response.GetResponseStream())
                        text = reader.ReadToEnd()
                    End Using
                End If

                Dim loc As String = response.Headers("Location")
                If loc Is Nothing Then
                    loc = ""
                End If

                Dim respUrl As String = NormalizeUrl(response.ResponseUri.ToString())
                Dim status As Integer = CInt(response.StatusCode)

                Dim locAbs As String = ""
                If Not String.IsNullOrWhiteSpace(loc) Then
                    locAbs =
                        NormalizeUrl(
                            New System.Uri(New System.Uri(respUrl), loc).ToString()
                        )
                End If

                Dim landedAuth As Boolean =
                    respUrl.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                    respUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                    locAbs.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                    locAbs.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0

                Console.WriteLine(
                    method & " -> HTTP " & status.ToString() &
                    " | bodyChars=" & text.Length.ToString() &
                    " | hasRedirect=" & (Not String.IsNullOrWhiteSpace(locAbs)).ToString() &
                    " | authPage=" & landedAuth.ToString()
                )

                Return New System.Tuple(Of String, String, Integer, String)(
                    text,
                    respUrl,
                    status,
                    locAbs
                )
            End Using
        End Function


    Dim getUrl As String =
        "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/CMIS/CMIS/EditProviderPlanOfCare/" &
        pocId &
        "?AssessmentId=" & System.Uri.EscapeDataString(assessId)

    Console.WriteLine("Built EditProviderPlanOfCare GET from planOfCareId + assessmentId.")
    Console.WriteLine("GET EditProviderPlanOfCare form...")

    Dim getResult =
        SendRequest(
            "GET",
            getUrl,
            Nothing,
            "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/CMIS/CMIS/ProvidersIndex"
        )

    responseHtml = If(getResult.Item1, "")
    finalUrl = getResult.Item2
    statusCode = getResult.Item3

    If statusCode <> 200 Then
        Throw New System.Exception(
            "EditProviderPlanOfCare GET failed. HTTP " & statusCode.ToString()
        )
    End If

    If finalUrl.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
       finalUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 Then
        Console.WriteLine("WARN: landed on auth page — login session likely missing/expired.")
        Throw New System.Exception(
            "EditProviderPlanOfCare redirected to auth. Session cookies are missing or expired."
        )
    End If

    If responseHtml.IndexOf("EditProviderPlanOfCare", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
       responseHtml.IndexOf("ProvidersServices", StringComparison.OrdinalIgnoreCase) < 0 Then
        Console.WriteLine("FAIL: page HTML missing EditProviderPlanOfCare / ProvidersServices markers.")
        Throw New System.Exception(
            "EditProviderPlanOfCare page HTML did not contain expected form fields."
        )
    End If

    Console.WriteLine("Edit form HTML received. Extracting fields...")

    Dim fields = ExtractFormFields(responseHtml)

    If fields.Count = 0 Then
        Console.WriteLine("FAIL: zero form fields extracted.")
        Throw New System.Exception("No form fields extracted from EditProviderPlanOfCare HTML.")
    End If

    Console.WriteLine("Form fields extracted: " & fields.Count.ToString())

    ' Ensure core ids from Invoke args
    fields("Id") = pocId
    fields("AssessmentId") = assessId

    fields("Issaveclose") = "true"

    Dim currentUserSid As String = ""
    Dim currentUserName As String = ""
    If fields.ContainsKey("CurrentUserSid") Then
        currentUserSid = fields("CurrentUserSid")
    End If
    If fields.ContainsKey("CurrentUserName") Then
        currentUserName = fields("CurrentUserName")
    End If

    Dim signatureDate As String =
        System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")

    ' ServiceStartDate on agreement rows often uses a full datetime
    Dim serviceStartDateValue As String = startDate
    Try
        Dim parsedStart As System.DateTime
        If System.DateTime.TryParse(startDate, parsedStart) Then
            serviceStartDateValue = parsedStart.ToString("M/d/yyyy") & " 12:00:00 AM"
        End If
    Catch
    End Try

    ' Discover ProvidersServices card indexes (acceptance cards only — not all Services[])
    Dim indexSet As New System.Collections.Generic.SortedSet(Of Integer)()
    For Each key As String In fields.Keys
        Dim im As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                key,
                "^ProvidersServices\[(\d+)\]\.Id$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )
        If im.Success Then
            indexSet.Add(Integer.Parse(im.Groups(1).Value))
        End If
    Next

    Dim htmlIndexMatches As System.Text.RegularExpressions.MatchCollection =
        System.Text.RegularExpressions.Regex.Matches(
            responseHtml,
            "name\s*=\s*[""']ProvidersServices\[(\d+)\]\.Id[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        )
    For Each hm As System.Text.RegularExpressions.Match In htmlIndexMatches
        indexSet.Add(Integer.Parse(hm.Groups(1).Value))
    Next

    If indexSet.Count = 0 Then
        Console.WriteLine("This document might have already been updated")
	Else

    serviceCount = indexSet.Count
    Dim acceptedCount As Integer = 0
    Dim skippedSigned As Integer = 0
    Dim servicesSynced As Integer = 0

    Console.WriteLine(
        "ProvidersServices cards found: " & serviceCount.ToString() &
        " (will skip already-signed; sync Services[i] by same index)."
    )

    For Each idx As Integer In indexSet
        Dim prefix As String = "ProvidersServices[" & idx.ToString() & "]"

        Dim existingSig As String = ""
        Dim existingSigDate As String = ""
        Dim existingAccept As String = ""
        If fields.ContainsKey(prefix & ".ProviderSignature") Then
            existingSig = If(fields(prefix & ".ProviderSignature"), "").Trim()
        End If
        If fields.ContainsKey(prefix & ".ProviderSignatureDate") Then
            existingSigDate = If(fields(prefix & ".ProviderSignatureDate"), "").Trim()
        End If
        If fields.ContainsKey(prefix & ".Accept") Then
            existingAccept = If(fields(prefix & ".Accept"), "").Trim()
        End If

        Dim alreadySigned As Boolean =
            (existingSig.Length > 0 AndAlso
             existingSig.IndexOf("data:image/", StringComparison.OrdinalIgnoreCase) >= 0) OrElse
            Not String.IsNullOrWhiteSpace(existingSigDate) OrElse
            existingAccept.Equals("True", StringComparison.OrdinalIgnoreCase)

        If alreadySigned Then
            skippedSigned += 1
            Continue For
        End If

        fields(prefix & ".Accept") = "True"
        fields(prefix & ".AnticipatedStartDate") = startDate
        fields(prefix & ".ServiceStartDate") = serviceStartDateValue

        ' Clear delay / decline so accept path does not leave null-ish reasons
        fields(prefix & ".ParticipantDelay") = "false"
        fields(prefix & ".ParticipantDelayExplanation") = ""
        fields(prefix & ".DelayReasonId") = "0"
        fields(prefix & ".DeclineReasonId") = "0"
        fields(prefix & ".DeclineReasonOther") = ""

        ' Signature on ProvidersServices card
        fields(prefix & ".ProviderSignature") = dataUrl
        fields(prefix & ".ProviderSignatureDate") = signatureDate

        If Not String.IsNullOrWhiteSpace(currentUserSid) Then
            fields(prefix & ".ProviderSignatureUserSid") = currentUserSid
        End If
        If Not String.IsNullOrWhiteSpace(currentUserName) Then
            fields(prefix & ".ProviderSignatureUserName") = currentUserName
        Else
            fields(prefix & ".ProviderSignatureUserName") = ""
        End If

        ' Sync matching Services[i] (data-service index maps PS[i] -> Services[i])
        Dim svcPrefix As String = "Services[" & idx.ToString() & "]"
        Dim psProviderName As String = ""
        If fields.ContainsKey(prefix & ".ProviderName") Then
            psProviderName = If(fields(prefix & ".ProviderName"), "").Trim()
        End If

        Dim svcExists As Boolean =
            fields.ContainsKey(svcPrefix & ".ProviderSignature") OrElse
            fields.ContainsKey(svcPrefix & ".ProviderName") OrElse
            fields.ContainsKey(svcPrefix & ".ServiceTypeId")

        Dim nameOk As Boolean = True
        If svcExists AndAlso
           Not String.IsNullOrWhiteSpace(psProviderName) AndAlso
           fields.ContainsKey(svcPrefix & ".ProviderName") Then

            Dim svcName As String = If(fields(svcPrefix & ".ProviderName"), "").Trim()
            If Not String.IsNullOrWhiteSpace(svcName) AndAlso
               Not svcName.Equals(psProviderName, StringComparison.OrdinalIgnoreCase) Then
                nameOk = False
                Console.WriteLine(
                    "WARN: Services[" & idx.ToString() & "] ProviderName mismatch — " &
                    "skipping Services sync for this card index."
                )
            End If
        End If

        If svcExists AndAlso nameOk Then
            fields(svcPrefix & ".ProviderSignature") = dataUrl
            fields(svcPrefix & ".ProviderSignatureDate") = signatureDate
            fields(svcPrefix & ".ServiceStartDate") = serviceStartDateValue
            servicesSynced += 1
        End If

        acceptedCount += 1
    Next

    If acceptedCount = 0 Then
        Console.WriteLine(
            "FAIL: no unsigned ProvidersServices cards to accept " &
            "(found=" & serviceCount.ToString() &
            ", alreadySigned=" & skippedSigned.ToString() & ")."
        )
        Throw New System.Exception(
            "No unsigned ProvidersServices cards to accept. " &
            "CardsFound=" & serviceCount.ToString() &
            " AlreadySigned=" & skippedSigned.ToString()
        )
    End If

    Console.WriteLine(
        "Cards prepared | accepted=" & acceptedCount.ToString() &
        " | skippedAlreadySigned=" & skippedSigned.ToString() &
        " | servicesSynced=" & servicesSynced.ToString() &
        " | Issaveclose=true | hasSignerSid=" &
        (Not String.IsNullOrWhiteSpace(currentUserSid)).ToString()
    )

    If String.IsNullOrWhiteSpace(pocId) Then
        Throw New System.Exception(
            "Plan of Care Id missing after GET — cannot POST Save & Close."
        )
    End If

    Dim postUrl As String =
        "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/CMIS/CMIS/EditProviderPlanOfCare/" &
        pocId

    Dim postBody As String = BuildFormBody(fields)

    Console.WriteLine(
        "POST Save & Close | fieldCount=" & fields.Count.ToString() &
        " | bodyChars=" & postBody.Length.ToString()
    )

    Dim postResult =
        SendRequest("POST", postUrl, postBody, getUrl)

    responseHtml = If(postResult.Item1, "")
    finalUrl = postResult.Item2
    statusCode = postResult.Item3
    Dim location As String = postResult.Item4

    If statusCode >= 300 AndAlso
       statusCode <= 399 AndAlso
       Not String.IsNullOrWhiteSpace(location) Then

        Dim toIndex As Boolean =
            location.IndexOf("ProvidersIndex", StringComparison.OrdinalIgnoreCase) >= 0
        Console.WriteLine(
            "Following redirect | toProvidersIndex=" & toIndex.ToString()
        )
        Dim follow =
            SendRequest("GET", location, Nothing, postUrl)

        responseHtml = If(follow.Item1, "")
        finalUrl = follow.Item2
        statusCode = follow.Item3
    End If

    If statusCode = 200 AndAlso
       finalUrl.IndexOf("ProvidersIndex", StringComparison.OrdinalIgnoreCase) >= 0 Then

        success = True
        Console.WriteLine("=== EditProviderPlanOfCare completed successfully ===")

    ElseIf statusCode >= 300 AndAlso
           statusCode <= 399 AndAlso
           Not String.IsNullOrWhiteSpace(location) AndAlso
           location.IndexOf("ProvidersIndex", StringComparison.OrdinalIgnoreCase) >= 0 Then

        success = True
        finalUrl = location
        Console.WriteLine("=== EditProviderPlanOfCare completed (302 ProvidersIndex) ===")

    Else
        ' Validation error page often returns 200 on same Edit URL
        Dim hasValidation As Boolean =
            responseHtml.IndexOf("field-validation-error", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            responseHtml.IndexOf("validation-summary-errors", StringComparison.OrdinalIgnoreCase) >= 0

        Console.WriteLine(
            "FAIL: save did not land on ProvidersIndex | HTTP=" & statusCode.ToString() &
            " | validationHints=" & hasValidation.ToString() &
            " | serviceCards=" & serviceCount.ToString()
        )

        Throw New System.Exception(
            "Save did not land on ProvidersIndex. HTTP " & statusCode.ToString() &
            " ValidationHints=" & hasValidation.ToString() &
            " ServiceCards=" & serviceCount.ToString()
        )
    End If
End If

Catch ex As Exception

    success = False
    errorMessage = ex.ToString()
    Console.WriteLine("=== EditProviderPlanOfCare FAILED ===")
    Console.WriteLine(
        "At failure: HTTP=" & statusCode.ToString() &
        " | htmlChars=" & responseHtml.Length.ToString() &
        " | serviceCards=" & serviceCount.ToString()
    )
    Console.WriteLine(errorMessage)

End Try
