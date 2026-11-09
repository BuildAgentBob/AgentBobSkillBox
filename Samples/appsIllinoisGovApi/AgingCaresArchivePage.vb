' GETs any AgingCares CaseManagement pageUrl with the shared cookieJar, downloads
' linked stylesheets + page images, inlines them into one self-contained .html,
' expands Bootstrap collapse panels (class "in"), fixes table print layout
' (col-md on th/td + agingcares-print-fix CSS), and writes outputHtmlPath.
' Same Invoke Code for ViewPlanOfCare, ParticipantHub, etc. — caller builds the
' full URL. cookies In/Out; errorMessage Out.

Dim responseHtml As String = ""
Dim statusCode As Integer = 0
Dim finalUrl As String = ""
Dim cssInlined As Integer = 0
Dim imagesInlined As Integer = 0
Dim success As Boolean = False

Try
    errorMessage = ""

    Console.WriteLine("=== AgingCares ArchivePage (self-contained HTML) ===")

    If cookies Is Nothing Then
        Console.WriteLine("ABORT: cookies CookieContainer is Nothing — run AgingCaresLogin first.")
        Throw New System.Exception(
            "cookies CookieContainer is required."
        )
    End If

    ' Local copy — ByRef Invoke args cannot be captured by lambdas
    Dim cookieJar As System.Net.CookieContainer = cookies

    Dim getUrl As String = If(pageUrl, "").Trim()
    Dim outPath As String = If(outputHtmlPath, "").Trim()

    If String.IsNullOrWhiteSpace(getUrl) Then
        Console.WriteLine("ABORT: pageUrl is missing.")
        Throw New System.Exception("pageUrl is required.")
    End If

    If String.IsNullOrWhiteSpace(outPath) Then
        Console.WriteLine("ABORT: outputHtmlPath is missing.")
        Throw New System.Exception("outputHtmlPath is required.")
    End If

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

    getUrl = NormalizeUrl(getUrl)

    If getUrl.IndexOf(
           "/AGE/AgingCares.CaseManagement/",
           StringComparison.OrdinalIgnoreCase
       ) < 0 Then
        Console.WriteLine("ABORT: pageUrl is not an AgingCares CaseManagement URL.")
        Throw New System.Exception(
            "pageUrl must be under /AGE/AgingCares.CaseManagement/."
        )
    End If

    Console.WriteLine("Inputs OK | outputSet=True | AgingCares pageUrl accepted.")


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


    Dim ToAbsoluteUrl As Func(Of String, String, String) =
        Function(href As String, baseUrl As String) As String
            If String.IsNullOrWhiteSpace(href) Then
                Return ""
            End If
            href = System.Net.WebUtility.HtmlDecode(href).Trim()
            If href.StartsWith("data:", StringComparison.OrdinalIgnoreCase) OrElse
               href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) OrElse
               href.StartsWith("#") Then
                Return href
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


    Dim GuessMime As Func(Of String, String, String) =
        Function(url As String, contentType As String) As String
            If Not String.IsNullOrWhiteSpace(contentType) Then
                Dim ct As String = contentType.Split(";"c)(0).Trim().ToLowerInvariant()
                If ct.Length > 0 AndAlso
                   Not ct.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase) AndAlso
                   Not ct.Equals("text/plain", StringComparison.OrdinalIgnoreCase) Then
                    Return ct
                End If
            End If
            Dim pathOnly As String = url
            Try
                pathOnly = New System.Uri(url).AbsolutePath
            Catch
            End Try
            Dim ext As String =
                System.IO.Path.GetExtension(pathOnly).ToLowerInvariant()
            Select Case ext
                Case ".css"
                    Return "text/css"
                Case ".png"
                    Return "image/png"
                Case ".jpg", ".jpeg"
                    Return "image/jpeg"
                Case ".gif"
                    Return "image/gif"
                Case ".svg"
                    Return "image/svg+xml"
                Case ".webp"
                    Return "image/webp"
                Case ".ico"
                    Return "image/x-icon"
                Case ".woff"
                    Return "font/woff"
                Case ".woff2"
                    Return "font/woff2"
                Case ".ttf"
                    Return "font/ttf"
                Case ".eot"
                    Return "application/vnd.ms-fontobject"
                Case Else
                    Return "application/octet-stream"
            End Select
        End Function


    Dim FetchBytes As Func(
        Of String,
        String,
        System.Tuple(Of Byte(), String, Integer, String)
    ) =
        Function(url As String, referer As String)

            url = NormalizeUrl(url)

            Dim req As System.Net.HttpWebRequest =
                CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)

            req.Method = "GET"
            req.CookieContainer = cookieJar
            req.AllowAutoRedirect = True
            req.KeepAlive = False
            req.Timeout = 180000
            req.ReadWriteTimeout = 180000

            req.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
                "AppleWebKit/537.36 (KHTML, like Gecko) " &
                "Chrome/153.0.0.0 Safari/537.36"

            req.Accept = "*/*"
            req.Headers(System.Net.HttpRequestHeader.AcceptLanguage) = "en-US,en;q=0.9"

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
                    Throw
                End If
                resp = CType(webEx.Response, System.Net.HttpWebResponse)
            End Try

            Using response As System.Net.HttpWebResponse = resp
                Dim bytes As Byte() = New Byte() {}
                If response.GetResponseStream() IsNot Nothing Then
                    Using ms As New System.IO.MemoryStream()
                        response.GetResponseStream().CopyTo(ms)
                        bytes = ms.ToArray()
                    End Using
                End If

                Dim ct As String = If(response.ContentType, "")
                Dim status As Integer = CInt(response.StatusCode)
                Dim respUrl As String = NormalizeUrl(response.ResponseUri.ToString())

                Return New System.Tuple(Of Byte(), String, Integer, String)(
                    bytes,
                    ct,
                    status,
                    respUrl
                )
            End Using
        End Function


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

                Dim landedAuth As Boolean =
                    respUrl.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                    respUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                    locAbs.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                    locAbs.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0

                Console.WriteLine(
                    "GET -> HTTP " & status.ToString() &
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


    Dim BytesToDataUrl As Func(Of Byte(), String, String) =
        Function(bytes As Byte(), mime As String) As String
            If bytes Is Nothing OrElse bytes.Length = 0 Then
                Return ""
            End If
            If String.IsNullOrWhiteSpace(mime) Then
                mime = "application/octet-stream"
            End If
            Return "data:" & mime & ";base64," &
                   System.Convert.ToBase64String(bytes)
        End Function


    Dim InlineCssUrls As Func(Of String, String, String, String) =
        Function(cssText As String, cssUrl As String, referer As String) As String
            If String.IsNullOrWhiteSpace(cssText) Then
                Return ""
            End If

            Return System.Text.RegularExpressions.Regex.Replace(
                cssText,
                "url\(\s*(?:""([^""]*)""|'([^']*)'|([^)\s]+))\s*\)",
                Function(m As System.Text.RegularExpressions.Match) As String
                    Dim raw As String = ""
                    If m.Groups(1).Success Then
                        raw = m.Groups(1).Value
                    ElseIf m.Groups(2).Success Then
                        raw = m.Groups(2).Value
                    Else
                        raw = m.Groups(3).Value
                    End If

                    raw = raw.Trim()
                    If String.IsNullOrWhiteSpace(raw) OrElse
                       raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then
                        Return m.Value
                    End If

                    Dim abs As String = ToAbsoluteUrl(raw, cssUrl)
                    If String.IsNullOrWhiteSpace(abs) OrElse
                       abs.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then
                        Return m.Value
                    End If

                    Dim pathOnly As String = abs
                    Try
                        pathOnly = New System.Uri(abs).AbsolutePath
                    Catch
                    End Try
                    Dim ext As String =
                        System.IO.Path.GetExtension(pathOnly).ToLowerInvariant()
                    Dim shouldInline As Boolean =
                        ext = ".png" OrElse ext = ".jpg" OrElse ext = ".jpeg" OrElse
                        ext = ".gif" OrElse ext = ".svg" OrElse ext = ".webp" OrElse
                        ext = ".ico" OrElse ext = ".woff" OrElse ext = ".woff2" OrElse
                        ext = ".ttf" OrElse ext = ".eot"

                    If Not shouldInline Then
                        Return "url(""" & abs & """)"
                    End If

                    Try
                        Dim asset = FetchBytes(abs, referer)
                        If asset.Item3 < 200 OrElse asset.Item3 >= 300 OrElse
                           asset.Item1 Is Nothing OrElse asset.Item1.Length = 0 Then
                            Return "url(""" & abs & """)"
                        End If
                        Dim mime As String = GuessMime(abs, asset.Item2)
                        Dim dataUrl As String = BytesToDataUrl(asset.Item1, mime)
                        imagesInlined += 1
                        Return "url(""" & dataUrl & """)"
                    Catch
                        Return "url(""" & abs & """)"
                    End Try
                End Function,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                System.Text.RegularExpressions.RegexOptions.Singleline
            )
        End Function


    Console.WriteLine("GET AgingCares page...")

    Dim getResult =
        SendHtmlRequest(
            getUrl,
            AgingCaresRoot & "CMIS/CMIS/ProvidersIndex"
        )

    responseHtml = If(getResult.Item1, "")
    finalUrl = getResult.Item2
    statusCode = getResult.Item3
    Dim location As String = getResult.Item4

    ' Follow one app redirect if needed (not auth)
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
            "AgingCares page GET failed. HTTP " & statusCode.ToString()
        )
    End If

    If finalUrl.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
       finalUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 Then
        Console.WriteLine("WARN: landed on auth page — login session likely missing/expired.")
        Throw New System.Exception(
            "Page redirected to auth. Session cookies are missing or expired."
        )
    End If

    If finalUrl.IndexOf(
           "/AGE/AgingCares.CaseManagement/",
           StringComparison.OrdinalIgnoreCase
       ) < 0 Then
        Throw New System.Exception(
            "Final URL left AgingCares CaseManagement — aborting archive."
        )
    End If

    Console.WriteLine(
        "AgingCares HTML received | chars=" & responseHtml.Length.ToString()
    )

    Dim pageBase As String = finalUrl
    If String.IsNullOrWhiteSpace(pageBase) Then
        pageBase = getUrl
    End If


    ' Inline <link rel=stylesheet> → <style>...</style> (keep media= when present)
    Console.WriteLine("Inlining linked stylesheets...")

    Dim archivedHtml As String =
        System.Text.RegularExpressions.Regex.Replace(
            responseHtml,
            "<link\b[^>]*>",
            Function(m As System.Text.RegularExpressions.Match) As String
                Dim tag As String = m.Value
                Dim rel As String = GetAttr(tag, "rel")
                Dim href As String = GetAttr(tag, "href")
                Dim media As String = GetAttr(tag, "media")

                Dim isStylesheet As Boolean =
                    rel.IndexOf("stylesheet", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                    (Not String.IsNullOrWhiteSpace(href) AndAlso
                     (href.IndexOf(".css", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                      href.IndexOf("/styles/css", StringComparison.OrdinalIgnoreCase) >= 0))

                If Not isStylesheet OrElse String.IsNullOrWhiteSpace(href) Then
                    Return tag
                End If

                Dim absCss As String = ToAbsoluteUrl(href, pageBase)
                If String.IsNullOrWhiteSpace(absCss) OrElse
                   absCss.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then
                    Return tag
                End If

                Try
                    Console.WriteLine("  GET stylesheet...")
                    Dim asset = FetchBytes(absCss, pageBase)
                    If asset.Item3 < 200 OrElse asset.Item3 >= 300 OrElse
                       asset.Item1 Is Nothing OrElse asset.Item1.Length = 0 Then
                        Console.WriteLine(
                            "  WARN: stylesheet HTTP " & asset.Item3.ToString() & " — left as link"
                        )
                        Return tag
                    End If

                    Dim cssText As String =
                        System.Text.Encoding.UTF8.GetString(asset.Item1)
                    cssText = InlineCssUrls(cssText, asset.Item4, pageBase)

                    Dim mediaAttr As String = ""
                    If Not String.IsNullOrWhiteSpace(media) Then
                        mediaAttr = " media=""" & media & """"
                    End If

                    cssInlined += 1
                    Console.WriteLine(
                        "  Inlined stylesheet | bytes=" & asset.Item1.Length.ToString() &
                        " | media=" & If(String.IsNullOrWhiteSpace(media), "(all)", media)
                    )

                    Return "<style type=""text/css""" & mediaAttr & ">" &
                           System.Environment.NewLine &
                           cssText &
                           System.Environment.NewLine &
                           "</style>"
                Catch exCss As System.Exception
                    Console.WriteLine("  WARN: stylesheet fetch failed — left as link")
                    Return tag
                End Try
            End Function,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
            System.Text.RegularExpressions.RegexOptions.Singleline
        )


    ' Inline <img src> that are not already data:
    Console.WriteLine("Inlining page images...")

    archivedHtml =
        System.Text.RegularExpressions.Regex.Replace(
            archivedHtml,
            "<img\b[^>]*>",
            Function(m As System.Text.RegularExpressions.Match) As String
                Dim tag As String = m.Value
                Dim src As String = GetAttr(tag, "src")
                If String.IsNullOrWhiteSpace(src) OrElse
                   src.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then
                    Return tag
                End If

                Dim absImg As String = ToAbsoluteUrl(src, pageBase)
                If String.IsNullOrWhiteSpace(absImg) OrElse
                   absImg.StartsWith("data:", StringComparison.OrdinalIgnoreCase) Then
                    Return tag
                End If

                Try
                    Dim asset = FetchBytes(absImg, pageBase)
                    If asset.Item3 < 200 OrElse asset.Item3 >= 300 OrElse
                       asset.Item1 Is Nothing OrElse asset.Item1.Length = 0 Then
                        Return tag
                    End If

                    Dim mime As String = GuessMime(absImg, asset.Item2)
                    Dim dataUrl As String = BytesToDataUrl(asset.Item1, mime)
                    If String.IsNullOrWhiteSpace(dataUrl) Then
                        Return tag
                    End If

                    imagesInlined += 1
                    Dim newTag As String =
                        System.Text.RegularExpressions.Regex.Replace(
                            tag,
                            "src\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
                            "src=""" & dataUrl & """",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        )
                    Return newTag
                Catch
                    Return tag
                End Try
            End Function,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
            System.Text.RegularExpressions.RegexOptions.Singleline
        )


    ' Strip scripts — not needed for offline open/print
    archivedHtml =
        System.Text.RegularExpressions.Regex.Replace(
            archivedHtml,
            "<script\b[^>]*>[\s\S]*?</script>",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        )
    archivedHtml =
        System.Text.RegularExpressions.Regex.Replace(
            archivedHtml,
            "<script\b[^>]*/>",
            "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        )


    ' Drop leftover agingcares-print-fix from older runs
    archivedHtml = System.Text.RegularExpressions.Regex.Replace(
        archivedHtml,
        "<style\b[^>]*id\s*=\s*[""']agingcares-print-fix[""'][^>]*>[\s\S]*?</style>",
        "",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase
    )


    ' Expand all Bootstrap 3 collapse panels (e.g. Participant Search Results #pss)
    Console.WriteLine("Expanding Bootstrap collapse panels...")
    Dim collapseExpanded As Integer = 0
    archivedHtml =
        System.Text.RegularExpressions.Regex.Replace(
            archivedHtml,
            "(<[^>]+\bclass\s*=\s*[""'])([^""']*)([""'][^>]*>)",
            Function(m As System.Text.RegularExpressions.Match) As String
                Dim cls As String = m.Groups(2).Value
                If Not System.Text.RegularExpressions.Regex.IsMatch(
                       cls,
                       "(^|\s)collapse(\s|$)",
                       System.Text.RegularExpressions.RegexOptions.IgnoreCase
                   ) Then
                    Return m.Value
                End If
                If System.Text.RegularExpressions.Regex.IsMatch(
                       cls,
                       "(^|\s)in(\s|$)",
                       System.Text.RegularExpressions.RegexOptions.IgnoreCase
                   ) Then
                    Return m.Value
                End If
                collapseExpanded += 1
                Return m.Groups(1).Value &
                       cls.TrimEnd() &
                       " in" &
                       m.Groups(3).Value
            End Function,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
            System.Text.RegularExpressions.RegexOptions.Singleline
        )

    archivedHtml =
        System.Text.RegularExpressions.Regex.Replace(
            archivedHtml,
            "aria-expanded\s*=\s*(?:""false""|'false')",
            "aria-expanded=""true""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        )

    Console.WriteLine(
        "Collapse panels expanded | count=" & collapseExpanded.ToString()
    )


    ' Bootstrap grid classes on <th>/<td> break table layout in print (eCCPIS hub).
    Console.WriteLine("Stripping Bootstrap col-* from table cells...")
    Dim tableColClassesStripped As Integer = 0
    archivedHtml =
        System.Text.RegularExpressions.Regex.Replace(
            archivedHtml,
            "(<(th|td)\b[^>]*\bclass\s*=\s*[""'])([^""']*)([""'][^>]*>)",
            Function(m As System.Text.RegularExpressions.Match) As String
                Dim cls As String = m.Groups(3).Value
                Dim stripped As String =
                    System.Text.RegularExpressions.Regex.Replace(
                        cls,
                        "\bcol-(?:xs|sm|md|lg)-\d+\b",
                        "",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    ).Trim()
                stripped =
                    System.Text.RegularExpressions.Regex.Replace(
                        stripped,
                        "\s{2,}",
                        " "
                    ).Trim()
                If String.Equals(cls, stripped, StringComparison.Ordinal) Then
                    Return m.Value
                End If
                tableColClassesStripped += 1
                If String.IsNullOrWhiteSpace(stripped) Then
                    Dim withoutClass As String =
                        System.Text.RegularExpressions.Regex.Replace(
                            m.Value,
                            "\s*class\s*=\s*[""'][^""']*[""']",
                            "",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        )
                    Return withoutClass
                End If
                Return m.Groups(1).Value & stripped & m.Groups(4).Value
            End Function,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
            System.Text.RegularExpressions.RegexOptions.Singleline
        )

    Console.WriteLine(
        "Table cell col-* stripped | count=" &
        tableColClassesStripped.ToString()
    )


    Console.WriteLine("Injecting print/layout fix CSS...")
    Dim printCss As String =
        "<style id=""agingcares-print-fix"" type=""text/css"">" &
        System.Environment.NewLine &
        "@page { size: tabloid landscape; margin: 0; }" &
        System.Environment.NewLine &
        "@media print {" &
        System.Environment.NewLine &
        "  html, body {" &
        System.Environment.NewLine &
        "    background: #fff !important;" &
        System.Environment.NewLine &
        "    overflow: visible !important;" &
        System.Environment.NewLine &
        "  }" &
        System.Environment.NewLine &
        "  .container, .container-fluid, #wrapper, .wrapper," &
        System.Environment.NewLine &
        "  #content, .main-content, .page-content {" &
        System.Environment.NewLine &
        "    border: none !important;" &
        System.Environment.NewLine &
        "    box-shadow: none !important;" &
        System.Environment.NewLine &
        "    outline: none !important;" &
        System.Environment.NewLine &
        "  }" &
        System.Environment.NewLine &
        "  .table-responsive {" &
        System.Environment.NewLine &
        "    width: 100% !important;" &
        System.Environment.NewLine &
        "    max-width: 100% !important;" &
        System.Environment.NewLine &
        "    overflow: visible !important;" &
        System.Environment.NewLine &
        "    border: none !important;" &
        System.Environment.NewLine &
        "  }" &
        System.Environment.NewLine &
        "  table.table {" &
        System.Environment.NewLine &
        "    max-width: 100% !important;" &
        System.Environment.NewLine &
        "  }" &
        System.Environment.NewLine &
        "  #pss table.table, #eCCPIS table.table {" &
        System.Environment.NewLine &
        "    width: 100% !important;" &
        System.Environment.NewLine &
        "    table-layout: fixed !important;" &
        System.Environment.NewLine &
        "  }" &
        System.Environment.NewLine &
        "  table.table th, table.table td {" &
        System.Environment.NewLine &
        "    float: none !important;" &
        System.Environment.NewLine &
        "    display: table-cell !important;" &
        System.Environment.NewLine &
        "    width: auto !important;" &
        System.Environment.NewLine &
        "    overflow-wrap: break-word !important;" &
        System.Environment.NewLine &
        "    word-wrap: break-word !important;" &
        System.Environment.NewLine &
        "    word-break: normal !important;" &
        System.Environment.NewLine &
        "  }" &
        System.Environment.NewLine &
        "  .col-print-1 { width: 8.33333333% !important; }" &
        System.Environment.NewLine &
        "  .col-print-2 { width: 16.66666667% !important; }" &
        System.Environment.NewLine &
        "  .col-print-3 { width: 25% !important; }" &
        System.Environment.NewLine &
        "  .col-print-4 { width: 33.33333333% !important; }" &
        System.Environment.NewLine &
        "  .col-print-5 { width: 41.66666667% !important; }" &
        System.Environment.NewLine &
        "  .col-print-6 { width: 50% !important; }" &
        System.Environment.NewLine &
        "  .col-print-7 { width: 58.33333333% !important; }" &
        System.Environment.NewLine &
        "  .col-print-8 { width: 66.66666667% !important; }" &
        System.Environment.NewLine &
        "  .col-print-9 { width: 75% !important; }" &
        System.Environment.NewLine &
        "  .col-print-10 { width: 83.33333333% !important; }" &
        System.Environment.NewLine &
        "  .col-print-11 { width: 91.66666667% !important; }" &
        System.Environment.NewLine &
        "  .col-print-12 { width: 100% !important; }" &
        System.Environment.NewLine &
        "}" &
        System.Environment.NewLine &
        "</style>"

    If System.Text.RegularExpressions.Regex.IsMatch(
           archivedHtml,
           "</head>",
           System.Text.RegularExpressions.RegexOptions.IgnoreCase
       ) Then
        archivedHtml =
            System.Text.RegularExpressions.Regex.Replace(
                archivedHtml,
                "</head>",
                printCss & System.Environment.NewLine & "</head>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )
    Else
        archivedHtml = printCss & System.Environment.NewLine & archivedHtml
    End If


    Dim outDir As String = System.IO.Path.GetDirectoryName(outPath)
    If Not String.IsNullOrWhiteSpace(outDir) AndAlso
       Not System.IO.Directory.Exists(outDir) Then
        System.IO.Directory.CreateDirectory(outDir)
        Console.WriteLine("Created output folder.")
    End If

    System.IO.File.WriteAllText(
        outPath,
        archivedHtml,
        System.Text.Encoding.UTF8
    )

    Dim fileInfo As New System.IO.FileInfo(outPath)
    Console.WriteLine(
        "Wrote archive HTML | cssInlined=" & cssInlined.ToString() &
        " | imagesInlined=" & imagesInlined.ToString() &
        " | fileBytes=" & fileInfo.Length.ToString()
    )

    success = True
    Console.WriteLine("=== AgingCares ArchivePage completed successfully ===")

Catch ex As Exception

    success = False
    errorMessage = ex.ToString()
    Console.WriteLine("=== AgingCares ArchivePage FAILED ===")
    Console.WriteLine(
        "At failure: HTTP=" & statusCode.ToString() &
        " | htmlChars=" & responseHtml.Length.ToString() &
        " | cssInlined=" & cssInlined.ToString() &
        " | imagesInlined=" & imagesInlined.ToString()
    )
    Console.WriteLine(errorMessage)

End Try
