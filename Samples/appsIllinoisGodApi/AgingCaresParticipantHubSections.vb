' GETs ViewProviderParticipantHubUrl, parses section view links (Demographics,
' Nutrition, Plan of Care, etc.), and returns participantHubUrls as
' Dictionary(Of String, Object): key = sanitizedSectionName + "_" + processingTag
' (e.g. Transport_myTag), value = absolute URL string.
' In: ViewProviderParticipantHubUrl, processingTag, cookies In/Out; errorMessage Out.

Dim responseHtml As String = ""
Dim statusCode As Integer = 0
Dim finalUrl As String = ""
Dim success As Boolean = False

Try
    errorMessage = ""

    Console.WriteLine("=== AgingCares ParticipantHubSections ===")

    If cookies Is Nothing Then
        Console.WriteLine("ABORT: cookies CookieContainer is Nothing — run AgingCaresLogin first.")
        Throw New System.Exception("cookies CookieContainer is required.")
    End If

    Dim cookieJar As System.Net.CookieContainer = cookies

    Dim getUrl As String = If(ViewProviderParticipantHubUrl, "").Trim()
    Dim processingTagKey As String = ""

    Const AgingCaresRoot As String =
        "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/"

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


    Dim ToAbsoluteUrl As Func(Of String, String, String) =
        Function(href As String, baseUrl As String) As String
            If String.IsNullOrWhiteSpace(href) Then
                Return ""
            End If
            href = System.Net.WebUtility.HtmlDecode(href).Trim()
            If href.StartsWith("#") OrElse
               href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) Then
                Return ""
            End If
            If href.StartsWith("//") Then
                Return "https:" & href
            End If
            Try
                Dim baseUri As New System.Uri(
                    If(String.IsNullOrWhiteSpace(baseUrl), AgingCaresRoot, baseUrl)
                )
                Return NormalizeUrl(New System.Uri(baseUri, href).ToString())
            Catch
                Return href
            End Try
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


    Dim SanitizeKey As Func(Of String, String) =
        Function(name As String) As String
            If String.IsNullOrWhiteSpace(name) Then
                Return ""
            End If
            Dim decoded As String = System.Net.WebUtility.HtmlDecode(name)
            decoded = System.Text.RegularExpressions.Regex.Replace(decoded, "\s+", " ").Trim()
            ' Letters and digits only — no spaces / punctuation
            Return System.Text.RegularExpressions.Regex.Replace(
                decoded,
                "[^A-Za-z0-9]",
                ""
            )
        End Function

    processingTagKey = SanitizeKey(If(processingTag, "").Trim())
    If String.IsNullOrWhiteSpace(processingTagKey) Then
        Console.WriteLine("ABORT: processingTag is missing or has no letters/digits.")
        Throw New System.Exception("processingTag is required.")
    End If

    Dim StripTags As Func(Of String, String) =
        Function(html As String) As String
            If String.IsNullOrWhiteSpace(html) Then
                Return ""
            End If
            Dim text As String =
                System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
            text = System.Net.WebUtility.HtmlDecode(text)
            text = System.Text.RegularExpressions.Regex.Replace(text, "\s+", " ").Trim()
            Return text
        End Function


    If String.IsNullOrWhiteSpace(getUrl) Then
        Console.WriteLine("ABORT: ViewProviderParticipantHubUrl is missing.")
        Throw New System.Exception("ViewProviderParticipantHubUrl is required.")
    End If

    getUrl = NormalizeUrl(getUrl)

    If getUrl.IndexOf(
           "ViewProviderParticipantHub",
           StringComparison.OrdinalIgnoreCase
       ) < 0 Then
        Throw New System.Exception(
            "ViewProviderParticipantHubUrl must contain ViewProviderParticipantHub."
        )
    End If

    If getUrl.IndexOf(
           "/AGE/AgingCares.CaseManagement/",
           StringComparison.OrdinalIgnoreCase
       ) < 0 Then
        Throw New System.Exception(
            "ViewProviderParticipantHubUrl must be under /AGE/AgingCares.CaseManagement/."
        )
    End If

    Console.WriteLine("Inputs OK | ViewProviderParticipantHubUrl set.")


    Dim SendHtmlRequest As Func(
        Of String,
        String,
        System.Tuple(Of String, String, Integer, String)
    ) =
        Function(url As String, referer As String)

            url = NormalizeUrl(url)

            Dim req As System.Net.HttpWebRequest =
                CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)

            req.Method = "GET"
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

            If Not String.IsNullOrWhiteSpace(referer) Then
                req.Referer = NormalizeUrl(referer)
            End If

            req.AutomaticDecompression =
                System.Net.DecompressionMethods.GZip Or
                System.Net.DecompressionMethods.Deflate

            Dim resp As System.Net.HttpWebResponse = Nothing
            Try
                resp = CType(req.GetResponse(), System.Net.HttpWebResponse)
            Catch webEx As System.Net.WebException
                If webEx.Response Is Nothing Then
                    Console.WriteLine("GET failed with no HTTP response: " & webEx.Message)
                    Throw
                End If
                Console.WriteLine(
                    "GET returned error status (will still read body): " & webEx.Message
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

                Console.WriteLine(
                    "GET -> HTTP " & status.ToString() &
                    " | bodyChars=" & text.Length.ToString() &
                    " | hasRedirect=" & (Not String.IsNullOrWhiteSpace(locAbs)).ToString()
                )

                Return New System.Tuple(Of String, String, Integer, String)(
                    text,
                    respUrl,
                    status,
                    locAbs
                )
            End Using
        End Function


    Console.WriteLine("GET ViewProviderParticipantHub...")

    Dim getResult =
        SendHtmlRequest(
            getUrl,
            AgingCaresRoot & "CMIS/CMIS/ProvidersIndex"
        )

    responseHtml = If(getResult.Item1, "")
    finalUrl = getResult.Item2
    statusCode = getResult.Item3
    Dim location As String = getResult.Item4

    If statusCode >= 300 AndAlso statusCode <= 399 AndAlso
       Not String.IsNullOrWhiteSpace(location) AndAlso
       location.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
       location.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) < 0 Then

        Console.WriteLine("Following redirect...")
        getResult = SendHtmlRequest(location, getUrl)
        responseHtml = If(getResult.Item1, "")
        finalUrl = getResult.Item2
        statusCode = getResult.Item3
    End If

    If statusCode <> 200 Then
        Throw New System.Exception(
            "ViewProviderParticipantHub GET failed. HTTP " & statusCode.ToString()
        )
    End If

    If finalUrl.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
       finalUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 Then
        Throw New System.Exception(
            "Hub redirected to auth. Session cookies are missing or expired."
        )
    End If

    Dim pageBase As String = finalUrl
    If String.IsNullOrWhiteSpace(pageBase) Then
        pageBase = getUrl
    End If

    Console.WriteLine("Parsing section view links...")

    ' Match each <a ...> and a short following slice for the <b> section label
    Dim linkMatches As System.Text.RegularExpressions.MatchCollection =
        System.Text.RegularExpressions.Regex.Matches(
            responseHtml,
            "<a\b[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
            System.Text.RegularExpressions.RegexOptions.Singleline
        )

    Dim added As Integer = 0

    For Each linkMatch As System.Text.RegularExpressions.Match In linkMatches
        Dim tag As String = linkMatch.Value
        Dim hrefRaw As String = linkMatch.Groups(1).Value
        Dim abs As String = ToAbsoluteUrl(hrefRaw, pageBase)
        If String.IsNullOrWhiteSpace(abs) Then
            Continue For
        End If

        Dim pathLower As String = abs.ToLowerInvariant()

        ' Only assessment / POC / case-notes style view links on this hub
        Dim isSectionLink As Boolean =
            pathLower.IndexOf("/viewassessment_", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            pathLower.IndexOf("/viewphysicalhealhassessement/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            pathLower.IndexOf("/viewindividualizedbackupplan/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            pathLower.IndexOf("/viewplanofcare/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            pathLower.IndexOf("/casenotes", StringComparison.OrdinalIgnoreCase) >= 0

        If Not isSectionLink Then
            Continue For
        End If

        ' Skip edit/update POC — keep ViewPlanOfCare only
        If pathLower.IndexOf("/updateproviderplanofcare/", StringComparison.OrdinalIgnoreCase) >= 0 Then
            Continue For
        End If

        Dim title As String = GetAttr(tag, "title")
        Dim sectionLabel As String = ""

        ' Prefer bold label in the next ~500 chars (e.g. "Demographics")
        Dim afterStart As Integer = linkMatch.Index + linkMatch.Length
        Dim sliceLen As Integer = Math.Min(500, responseHtml.Length - afterStart)
        If sliceLen > 0 Then
            Dim after As String = responseHtml.Substring(afterStart, sliceLen)
            Dim boldMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    after,
                    "<b>\s*([^<]+?)\s*</b>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                )
            If boldMatch.Success Then
                sectionLabel = StripTags(boldMatch.Groups(1).Value)
            End If
        End If

        If String.IsNullOrWhiteSpace(sectionLabel) AndAlso
           Not String.IsNullOrWhiteSpace(title) Then
            sectionLabel = title
            If sectionLabel.StartsWith("View ", StringComparison.OrdinalIgnoreCase) Then
                sectionLabel = sectionLabel.Substring(5).Trim()
            End If
            ' Hub uses title="View OutCome" for Plan of Care
            If sectionLabel.Equals("OutCome", StringComparison.OrdinalIgnoreCase) OrElse
               sectionLabel.Equals("Outcome", StringComparison.OrdinalIgnoreCase) Then
                sectionLabel = "PlanOfCare"
            End If
        End If

        If String.IsNullOrWhiteSpace(sectionLabel) Then
            ' Fallback from path segment
            Dim pathMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    abs,
                    "/CMIS/CMIS/([^/?]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                )
            If pathMatch.Success Then
                sectionLabel = pathMatch.Groups(1).Value
            End If
        End If

        Dim sectionKeyBase As String = SanitizeKey(sectionLabel)
        If String.IsNullOrWhiteSpace(sectionKeyBase) Then
            Continue For
        End If

        Dim key As String = sectionKeyBase & "_" & processingTagKey

        ' Prefer ViewPlanOfCare if duplicate PlanOfCare keys appear
        If participantHubUrls.ContainsKey(key) Then
            Dim existing As String = Convert.ToString(participantHubUrls(key))
            Dim existingLower As String = If(existing, "").ToLowerInvariant()
            If sectionKeyBase.Equals("PlanOfCare", StringComparison.OrdinalIgnoreCase) AndAlso
               pathLower.IndexOf("/viewplanofcare/", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
               existingLower.IndexOf("/viewplanofcare/", StringComparison.OrdinalIgnoreCase) < 0 Then
                participantHubUrls(key) = abs
                Console.WriteLine("  Updated section | key=" & key)
            Else
                Console.WriteLine("  Skip duplicate section key | key=" & key)
            End If
            Continue For
        End If

        participantHubUrls(key) = abs
        added += 1
        Console.WriteLine("  Section | key=" & key)
    Next

    Console.WriteLine(
        "Parsed section links | count=" & participantHubUrls.Count.ToString()
    )

    If participantHubUrls.Count = 0 Then
        Throw New System.Exception(
            "No section view links found on ViewProviderParticipantHub."
        )
    End If

    success = True
    Console.WriteLine("=== ParticipantHubSections completed successfully ===")

Catch ex As Exception

    success = False
    If participantHubUrls Is Nothing Then
        participantHubUrls = New System.Collections.Generic.Dictionary(Of String, Object)(
            StringComparer.OrdinalIgnoreCase
        )
    End If
    errorMessage = ex.ToString()
    Console.WriteLine("=== ParticipantHubSections FAILED ===")
    Console.WriteLine(
        "At failure: HTTP=" & statusCode.ToString() &
        " | htmlChars=" & responseHtml.Length.ToString()
    )
    Console.WriteLine(errorMessage)

End Try
