' Gets the AgingCares ProvidersIndex page using the shared CookieContainer session
' and builds dtPendingProviders with each pending provider row (name, DOB, type,
' referred, county, RowId, PlanOfCareId, AssessmentId, ProviderNo, AddUrl, PocUrl,
' AllLinks). On failure, errorMessage is set; cookies and dtPendingProviders are
' Invoke Code arguments.

Dim responseHtml As String = ""
Dim statusCode As Integer = 0
Dim finalUrl As String = ""

Try
    errorMessage = ""

    dtPendingProviders = New System.Data.DataTable()
    dtPendingProviders.Columns.Add("Name", GetType(String))
    dtPendingProviders.Columns.Add("DOB", GetType(String))
    dtPendingProviders.Columns.Add("Type", GetType(String))
    dtPendingProviders.Columns.Add("Referred", GetType(String))
    dtPendingProviders.Columns.Add("County", GetType(String))
    dtPendingProviders.Columns.Add("RowId", GetType(String))
    dtPendingProviders.Columns.Add("PlanOfCareId", GetType(String))
    dtPendingProviders.Columns.Add("AssessmentId", GetType(String))
    dtPendingProviders.Columns.Add("ProviderNo", GetType(String))
    dtPendingProviders.Columns.Add("AddUrl", GetType(String))
    dtPendingProviders.Columns.Add("PocUrl", GetType(String))
    dtPendingProviders.Columns.Add("AllLinks", GetType(String))

    If cookies Is Nothing Then
        Console.WriteLine("ABORT: cookies CookieContainer is Nothing — run AgingCaresLogin first.")
        Throw New System.Exception(
            "cookies CookieContainer is required. Run AgingCaresLogin first " &
            "and pass the same CookieContainer In/Out."
        )
    End If

    Dim targetUrl As String =
        "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/CMIS/CMIS/ProvidersIndex"

    Console.WriteLine("=== AgingCares ProvidersIndex ===")
    Console.WriteLine("Session cookies present. Requesting pending-providers page...")

    System.Net.ServicePointManager.SecurityProtocol =
        System.Net.SecurityProtocolType.Tls12

    Try
        System.Net.ServicePointManager.SecurityProtocol =
            System.Net.SecurityProtocolType.Tls12 Or
            CType(12288, System.Net.SecurityProtocolType)
    Catch
    End Try

    System.Net.ServicePointManager.Expect100Continue = False

    Dim req As System.Net.HttpWebRequest =
        CType(System.Net.WebRequest.Create(targetUrl), System.Net.HttpWebRequest)

    req.Method = "GET"
    req.CookieContainer = cookies
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
    req.Referer =
        "https://webapps.illinois.gov/CMS/SP2/?wa=wsignin1.0&wtrealm=" &
        System.Uri.EscapeDataString(
            "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/"
        )

    req.AutomaticDecompression =
        System.Net.DecompressionMethods.GZip Or
        System.Net.DecompressionMethods.Deflate

    Console.WriteLine("GET " & targetUrl)

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

        statusCode = CInt(response.StatusCode)
        finalUrl = response.ResponseUri.ToString().Replace(":443/", "/").Replace(":443?", "?")

        If response.GetResponseStream() IsNot Nothing Then
            Using reader As New System.IO.StreamReader(response.GetResponseStream())
                responseHtml = reader.ReadToEnd()
            End Using
        End If

        Console.WriteLine(
            "Response: HTTP " & statusCode.ToString() &
            " | finalUrl=" & finalUrl &
            " | htmlChars=" & responseHtml.Length.ToString()
        )

        If statusCode <> 200 Then
            Throw New System.Exception(
                "ProvidersIndex did not return HTTP 200. Status=" &
                statusCode.ToString() &
                " FinalUrl=" & finalUrl
            )
        End If

        If finalUrl.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
           finalUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 Then

            Console.WriteLine(
                "WARN: landed on auth page — login session likely missing/expired."
            )
            Throw New System.Exception(
                "ProvidersIndex redirected to auth. Session cookies are missing or expired. " &
                "FinalUrl=" & finalUrl
            )
        End If

        Console.WriteLine("Authenticated ProvidersIndex HTML received.")

    End Using


    ' Extract Pending Providers table (#POCTable) into DataTable
    Console.WriteLine("Looking for #POCTable (Pending Providers)...")

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

    Dim ToAbsoluteUrl As Func(Of String, String) =
        Function(href As String) As String
            If String.IsNullOrWhiteSpace(href) Then
                Return ""
            End If
            href = System.Net.WebUtility.HtmlDecode(href).Trim()
            If href.StartsWith("#") OrElse
               href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) Then
                Return ""
            End If
            Try
                Return New System.Uri(
                    New System.Uri("https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/"),
                    href
                ).ToString()
            Catch
                Return href
            End Try
        End Function

    Dim GetQueryParam As Func(Of String, String, String) =
        Function(url As String, key As String) As String
            If String.IsNullOrWhiteSpace(url) OrElse String.IsNullOrWhiteSpace(key) Then
                Return ""
            End If
            Try
                Dim q As String = New System.Uri(url).Query
                If String.IsNullOrEmpty(q) Then
                    Return ""
                End If
                If q.StartsWith("?") Then
                    q = q.Substring(1)
                End If
                For Each part As String In q.Split("&"c)
                    Dim kv As String() = part.Split(New Char() {"="c}, 2)
                    If kv.Length >= 1 AndAlso
                       System.Uri.UnescapeDataString(kv(0)).Equals(
                           key,
                           StringComparison.OrdinalIgnoreCase
                       ) Then
                        If kv.Length = 2 Then
                            Return System.Uri.UnescapeDataString(kv(1).Replace("+"c, " "c))
                        End If
                        Return ""
                    End If
                Next
            Catch
            End Try
            Return ""
        End Function

    Dim tableHtml As String = ""

    Dim tableMatch As System.Text.RegularExpressions.Match =
        System.Text.RegularExpressions.Regex.Match(
            responseHtml,
            "<table\b[^>]*\bid\s*=\s*[""']POCTable[""'][^>]*>.*?</table>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
            System.Text.RegularExpressions.RegexOptions.Singleline
        )

    If tableMatch.Success Then
        tableHtml = tableMatch.Value
        Console.WriteLine("Found table via #POCTable id.")
    Else
        Console.WriteLine("#POCTable not found — trying Pending Providers fallback...")
        tableMatch =
            System.Text.RegularExpressions.Regex.Match(
                responseHtml,
                "Pending\s+Providers.*?(<table\b[^>]*>.*?</table>)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                System.Text.RegularExpressions.RegexOptions.Singleline
            )
        If tableMatch.Success Then
            tableHtml = tableMatch.Groups(1).Value
            Console.WriteLine("Found table via Pending Providers heading fallback.")
        End If
    End If

    If String.IsNullOrWhiteSpace(tableHtml) Then
        Console.WriteLine(
            "FAIL: no Pending Providers table in HTML (htmlChars=" &
            responseHtml.Length.ToString() & ")."
        )
        Throw New System.Exception(
            "Pending Providers table (#POCTable) was not found in ProvidersIndex HTML."
        )
    End If

    Dim rowMatches As System.Text.RegularExpressions.MatchCollection =
        System.Text.RegularExpressions.Regex.Matches(
            tableHtml,
            "<tr\b[^>]*>.*?</tr>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
            System.Text.RegularExpressions.RegexOptions.Singleline
        )

    Console.WriteLine(
        "Table HTML chars=" & tableHtml.Length.ToString() &
        " | <tr> tags=" & rowMatches.Count.ToString() &
        " (includes header)."
    )

    Dim skippedHeader As Integer = 0
    Dim skippedShort As Integer = 0
    Dim skippedNoName As Integer = 0
    Dim missingIds As Integer = 0

    For Each rm As System.Text.RegularExpressions.Match In rowMatches

        Dim rowHtml As String = rm.Value

        ' Skip header rows
        If rowHtml.IndexOf("<th", StringComparison.OrdinalIgnoreCase) >= 0 Then
            skippedHeader += 1
            Continue For
        End If

        Dim cellMatches As System.Text.RegularExpressions.MatchCollection =
            System.Text.RegularExpressions.Regex.Matches(
                rowHtml,
                "<td\b[^>]*>(.*?)</td>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                System.Text.RegularExpressions.RegexOptions.Singleline
            )

        If cellMatches.Count < 6 Then
            skippedShort += 1
            Continue For
        End If

        Dim actionsHtml As String = cellMatches(0).Groups(1).Value
        Dim name As String = StripTags(cellMatches(1).Groups(1).Value)
        Dim dob As String = StripTags(cellMatches(2).Groups(1).Value)
        Dim typ As String = StripTags(cellMatches(3).Groups(1).Value)
        Dim referred As String = StripTags(cellMatches(4).Groups(1).Value)
        Dim county As String = StripTags(cellMatches(5).Groups(1).Value)

        If String.IsNullOrWhiteSpace(name) Then
            skippedNoName += 1
            Continue For
        End If

        ' --- All hrefs in Actions column (green + and POC >) ---
        Dim linkList As New System.Collections.Generic.List(Of String)()
        Dim addUrl As String = ""
        Dim pocUrl As String = ""

        Dim hrefMatches As System.Text.RegularExpressions.MatchCollection =
            System.Text.RegularExpressions.Regex.Matches(
                actionsHtml,
                "href\s*=\s*[""']([^""']+)[""']",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )

        For Each hm As System.Text.RegularExpressions.Match In hrefMatches
            Dim abs As String = ToAbsoluteUrl(hm.Groups(1).Value)
            If String.IsNullOrWhiteSpace(abs) Then
                Continue For
            End If
            If Not linkList.Contains(abs) Then
                linkList.Add(abs)
            End If

            Dim lower As String = abs.ToLowerInvariant()
            Dim labelHint As String = StripTags(actionsHtml)

            ' POC button / plan-of-care style links
            If lower.IndexOf("poc", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               lower.IndexOf("planofcare", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               lower.IndexOf("/cmis/", StringComparison.OrdinalIgnoreCase) >= 0 Then
                If String.IsNullOrWhiteSpace(pocUrl) OrElse
                   lower.IndexOf("poc", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    pocUrl = abs
                End If
            End If

            ' Green + / add / claim style links (first non-POC often)
            If String.IsNullOrWhiteSpace(addUrl) AndAlso
               (lower.IndexOf("add", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                lower.IndexOf("claim", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                lower.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                lower.IndexOf("plus", StringComparison.OrdinalIgnoreCase) >= 0) Then
                addUrl = abs
            End If
        Next

        ' If we still don't have add/poc split, use order: first link = +, second = POC
        If linkList.Count >= 1 AndAlso String.IsNullOrWhiteSpace(addUrl) Then
            addUrl = linkList(0)
        End If
        If linkList.Count >= 2 Then
            If String.IsNullOrWhiteSpace(pocUrl) Then
                pocUrl = linkList(1)
            End If
            ' Prefer last link containing POC text path if present
            For Each u As String In linkList
                If u.IndexOf("POC", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   u.IndexOf("PoC", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    pocUrl = u
                End If
            Next
        ElseIf linkList.Count = 1 AndAlso String.IsNullOrWhiteSpace(pocUrl) Then
            pocUrl = linkList(0)
        End If

        ' --- IDs from Claim(ProviderNo, AssessmentId, Id) onclick ---
        Dim providerNo As String = ""
        Dim assessmentId As String = ""
        Dim rowId As String = ""

        Dim claimMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                actionsHtml & " " & rowHtml,
                "Claim\s*\(\s*['""]?([^,'""\)]+)['""]?\s*,\s*['""]?([^,'""\)]+)['""]?\s*,\s*['""]?([^,'""\)]+)['""]?\s*\)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )

        If claimMatch.Success Then
            providerNo = claimMatch.Groups(1).Value.Trim()
            assessmentId = claimMatch.Groups(2).Value.Trim()
            rowId = claimMatch.Groups(3).Value.Trim()
        End If

        ' data-* attributes on the row / buttons
        If String.IsNullOrWhiteSpace(rowId) Then
            Dim dataId As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    actionsHtml & rowHtml,
                    "data-(?:id|rowid|assessmentid)\s*=\s*[""']([^""']+)[""']",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                )
            If dataId.Success Then
                rowId = dataId.Groups(1).Value.Trim()
            End If
        End If

        ' Query-string ids from any action link
        Dim idSourceUrls As New System.Collections.Generic.List(Of String)(linkList)
        idSourceUrls.Add(pocUrl)
        idSourceUrls.Add(addUrl)

        For Each u As String In idSourceUrls
            If String.IsNullOrWhiteSpace(u) Then
                Continue For
            End If
            If String.IsNullOrWhiteSpace(rowId) Then
                rowId = GetQueryParam(u, "id")
                If String.IsNullOrWhiteSpace(rowId) Then
                    rowId = GetQueryParam(u, "Id")
                End If
                If String.IsNullOrWhiteSpace(rowId) Then
                    rowId = GetQueryParam(u, "rowId")
                End If
            End If
            If String.IsNullOrWhiteSpace(assessmentId) Then
                assessmentId = GetQueryParam(u, "AssessmentId")
                If String.IsNullOrWhiteSpace(assessmentId) Then
                    assessmentId = GetQueryParam(u, "assessmentId")
                End If
            End If
            If String.IsNullOrWhiteSpace(providerNo) Then
                providerNo = GetQueryParam(u, "ProviderNo")
                If String.IsNullOrWhiteSpace(providerNo) Then
                    providerNo = GetQueryParam(u, "ProvNo")
                End If
            End If
        Next

        ' Plan of Care id from EditProviderPlanOfCare/{id} path (fallback: RowId)
        Dim planOfCareId As String = ""
        Dim pocIdMatch As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                If(pocUrl, "") & " " & String.Join(" ", linkList),
                "EditProviderPlanOfCare/(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )
        If pocIdMatch.Success Then
            planOfCareId = pocIdMatch.Groups(1).Value.Trim()
        ElseIf Not String.IsNullOrWhiteSpace(rowId) Then
            planOfCareId = rowId
        End If

        dtPendingProviders.Rows.Add(
            name,
            dob,
            typ,
            referred,
            county,
            rowId,
            planOfCareId,
            assessmentId,
            providerNo,
            addUrl,
            pocUrl,
            String.Join(" | ", linkList)
        )

        If String.IsNullOrWhiteSpace(planOfCareId) OrElse
           String.IsNullOrWhiteSpace(assessmentId) Then
            missingIds += 1
        End If

    Next

    Console.WriteLine("--- parse summary ---")
    Console.WriteLine(
        "Pending providers extracted: " & dtPendingProviders.Rows.Count.ToString()
    )
    Console.WriteLine(
        "Skipped: header=" & skippedHeader.ToString() &
        " | shortRow=" & skippedShort.ToString() &
        " | emptyName=" & skippedNoName.ToString()
    )
    If missingIds > 0 Then
        Console.WriteLine(
            "WARN: " & missingIds.ToString() &
            " row(s) missing PlanOfCareId and/or AssessmentId."
        )
    End If

    If dtPendingProviders.Rows.Count = 0 Then
        Console.WriteLine("WARN: table found but zero data rows — queue may be empty.")
    End If

    Console.WriteLine("=== ProvidersIndex completed successfully ===")

Catch ex As Exception

    If dtPendingProviders Is Nothing Then
        dtPendingProviders = New System.Data.DataTable()
    End If

    errorMessage = ex.ToString()
    Console.WriteLine("=== ProvidersIndex FAILED ===")
    Console.WriteLine(
        "At failure: HTTP=" & statusCode.ToString() &
        " | finalUrl=" & If(String.IsNullOrWhiteSpace(finalUrl), "(n/a)", finalUrl) &
        " | htmlChars=" & responseHtml.Length.ToString() &
        " | rowsSoFar=" & dtPendingProviders.Rows.Count.ToString()
    )
    Console.WriteLine(errorMessage)

End Try
