' ============================================================
' ILLINOIS AGINGCARES — Edit Provider Plan of Care (Accept + Sign)
' UiPath Invoke Code - VB.NET
'
' Marked action:
'   GET  .../EditProviderPlanOfCare/{id}?AssessmentId=...
'   Accept each ProvidersServices[i] card (In Home, etc.)
'   Set AnticipatedStartDate + ProviderSignature (PNG as data URL)
'   POST Save & Close (Issaveclose=true) -> 302 ProvidersIndex
'
' IN:
'   planOfCareId              As String   ' e.g. "44628"
'   assessmentId              As String   ' e.g. "151483"
'   anticipatedStartDate      As String   ' e.g. "10/07/2026" (mm/dd/yyyy)
'   signaturePngPath          As String   ' local .png path
'
' IN/OUT:
'   cookies                   As System.Net.CookieContainer
'
' OUT:
'   errorMessage              As String
'
' Local Dim: responseHtml, statusCode, finalUrl, serviceCount, success
' ============================================================

Dim responseHtml As String = ""
Dim statusCode As Integer = 0
Dim finalUrl As String = ""
Dim serviceCount As Integer = 0
Dim success As Boolean = False

Try
    errorMessage = ""

    If cookies Is Nothing Then
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
        Throw New System.Exception("planOfCareId is required.")
    End If

    If String.IsNullOrWhiteSpace(assessId) Then
        Throw New System.Exception("assessmentId is required.")
    End If

    If String.IsNullOrWhiteSpace(startDate) Then
        Throw New System.Exception("anticipatedStartDate is required (mm/dd/yyyy).")
    End If

    If String.IsNullOrWhiteSpace(pngPath) Then
        Throw New System.Exception("signaturePngPath is required.")
    End If

    If Not System.IO.File.Exists(pngPath) Then
        Throw New System.Exception("signature PNG not found: " & pngPath)
    End If

    Dim bytes As Byte() = System.IO.File.ReadAllBytes(pngPath)
    Dim dataUrl As String =
        "data:image/png;base64," &
        System.Convert.ToBase64String(bytes)

    Console.WriteLine("Starting marked action: AgingCares EditProviderPlanOfCare")

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
        Function(fieldMap)
            Return String.Join(
                "&",
                fieldMap.Select(
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
                    Throw
                End If
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

                Console.WriteLine(method & " " & url & " -> HTTP " & status.ToString())

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

    Console.WriteLine("GET EditProviderPlanOfCare/" & pocId)

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
            "EditProviderPlanOfCare GET failed. HTTP " & statusCode.ToString() &
            " Url=" & finalUrl
        )
    End If

    If responseHtml.IndexOf("EditProviderPlanOfCare", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
       responseHtml.IndexOf("ProvidersServices", StringComparison.OrdinalIgnoreCase) < 0 Then
        Throw New System.Exception(
            "EditProviderPlanOfCare page HTML did not contain expected form fields."
        )
    End If


    Dim fields = ExtractFormFields(responseHtml)

    If fields.Count = 0 Then
        Throw New System.Exception("No form fields extracted from EditProviderPlanOfCare HTML.")
    End If

    ' Ensure core ids
    If Not String.IsNullOrWhiteSpace(pocId) Then
        fields("Id") = pocId
    End If
    If Not String.IsNullOrWhiteSpace(assessId) Then
        fields("AssessmentId") = assessId
    End If

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

    ' Discover service card indexes: ProvidersServices[n].Id
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

    ' Also scan HTML if dictionary missed some
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
        Throw New System.Exception(
            "No ProvidersServices[i] cards found on the Plan of Care page."
        )
    End If

    serviceCount = indexSet.Count
    Console.WriteLine("ProvidersServices cards found: " & serviceCount.ToString())

    For Each idx As Integer In indexSet
        Dim prefix As String = "ProvidersServices[" & idx.ToString() & "]"

        fields(prefix & ".Accept") = "True"
        fields(prefix & ".AnticipatedStartDate") = startDate

        ' Signature (PNG as data URL) — same image on every card you accept
        fields(prefix & ".ProviderSignature") = dataUrl
        fields(prefix & ".ProviderSignatureDate") = signatureDate

        If Not String.IsNullOrWhiteSpace(currentUserSid) Then
            fields(prefix & ".ProviderSignatureUserSid") = currentUserSid
        End If
        If Not String.IsNullOrWhiteSpace(currentUserName) Then
            fields(prefix & ".ProviderSignatureUserName") = currentUserName
        End If

        ' Ensure ParticipantDelay posts as false if not set
        If Not fields.ContainsKey(prefix & ".ParticipantDelay") Then
            fields(prefix & ".ParticipantDelay") = "false"
        End If

        Console.WriteLine(
            "Accept card " & idx.ToString() &
            " | AnticipatedStartDate=" & startDate &
            " | SignatureDate=" & signatureDate
        )
    Next


    Dim postUrl As String =
        "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/CMIS/CMIS/EditProviderPlanOfCare/" &
        If(String.IsNullOrWhiteSpace(pocId), fields("Id"), pocId)

    Dim postBody As String = BuildFormBody(fields)

    Console.WriteLine(
        "POST EditProviderPlanOfCare/" & pocId &
        " | Fields=" & fields.Count.ToString() &
        " | BodyChars=" & postBody.Length.ToString()
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

        Console.WriteLine("FOLLOW REDIRECT " & location)
        Dim follow =
            SendRequest("GET", location, Nothing, postUrl)

        responseHtml = If(follow.Item1, "")
        finalUrl = follow.Item2
        statusCode = follow.Item3
    End If

    If statusCode = 200 AndAlso
       finalUrl.IndexOf("ProvidersIndex", StringComparison.OrdinalIgnoreCase) >= 0 Then

        success = True
        Console.WriteLine("AgingCares EditProviderPlanOfCare completed successfully.")

    ElseIf statusCode >= 300 AndAlso
           statusCode <= 399 AndAlso
           Not String.IsNullOrWhiteSpace(location) AndAlso
           location.IndexOf("ProvidersIndex", StringComparison.OrdinalIgnoreCase) >= 0 Then

        success = True
        finalUrl = location
        Console.WriteLine("AgingCares EditProviderPlanOfCare completed (redirect to ProvidersIndex).")

    Else
        ' Validation error page often returns 200 on same Edit URL
        Dim hasValidation As Boolean =
            responseHtml.IndexOf("field-validation-error", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            responseHtml.IndexOf("validation-summary-errors", StringComparison.OrdinalIgnoreCase) >= 0

        Throw New System.Exception(
            "Save did not land on ProvidersIndex. HTTP " & statusCode.ToString() &
            " FinalUrl=" & finalUrl &
            " ValidationHints=" & hasValidation.ToString() &
            " ServiceCards=" & serviceCount.ToString()
        )
    End If

    If Not success Then
        Throw New System.Exception(
            "EditProviderPlanOfCare did not complete successfully."
        )
    End If

    cookies = cookieJar

Catch ex As Exception

    success = False
    errorMessage = ex.ToString()
    Console.WriteLine("AgingCares EditProviderPlanOfCare failed.")
    Console.WriteLine(errorMessage)
    Throw

End Try
