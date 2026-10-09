' Opens the provider Edit Plan of Care page for the given plan and assessment IDs
' (use the same cookies from login). Returns the care coordinator’s printed name and
' signature date as MM/dd/yyyy. Fails with a clear message if that date is not on the page yet.

Dim responseHtml As String = ""
Dim statusCode As Integer = 0
Dim finalUrl As String = ""
Dim success As Boolean = False

Try
    errorMessage = ""
    careCoordinatorSignatureDate = ""
    careCoordinatorName = ""

    Console.WriteLine("=== AgingCares Get Care Coordinator Signature ===")

    If cookies Is Nothing Then
        Console.WriteLine("ABORT: cookies CookieContainer is Nothing — run AgingCaresLogin first.")
        Throw New System.Exception("cookies CookieContainer is required.")
    End If

    Dim cookieJar As System.Net.CookieContainer = cookies

    Dim pocId As String = If(planOfCareId, "").Trim()
    Dim assessId As String = If(assessmentId, "").Trim()

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


    Dim HtmlDecode As Func(Of String, String) =
        Function(s As String) As String
            If String.IsNullOrEmpty(s) Then Return s
            Return System.Net.WebUtility.HtmlDecode(s)
        End Function


    Dim GetInputValueById As Func(Of String, String, String) =
        Function(html As String, fieldId As String) As String
            If String.IsNullOrWhiteSpace(html) OrElse String.IsNullOrWhiteSpace(fieldId) Then
                Return ""
            End If
            Dim esc As String =
                System.Text.RegularExpressions.Regex.Escape(fieldId)
            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "id\s*=\s*""" & esc & """[^>]*\bvalue\s*=\s*""([^""]*)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                )
            If Not m.Success Then
                m = System.Text.RegularExpressions.Regex.Match(
                    html,
                    "\bvalue\s*=\s*""([^""]*)""[^>]*\bid\s*=\s*""" & esc & """",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                )
            End If
            If Not m.Success Then
                Return ""
            End If
            Return HtmlDecode(m.Groups(1).Value).Trim()
        End Function


    Dim GetSpanInnerText As Func(Of String, String, String) =
        Function(html As String, spanId As String) As String
            If String.IsNullOrWhiteSpace(html) OrElse String.IsNullOrWhiteSpace(spanId) Then
                Return ""
            End If
            Dim esc As String =
                System.Text.RegularExpressions.Regex.Escape(spanId)
            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "id\s*=\s*""" & esc & """[^>]*>([\s\S]*?)</span>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                )
            If Not m.Success Then
                Return ""
            End If
            Dim inner As String = m.Groups(1).Value
            inner = System.Text.RegularExpressions.Regex.Replace(inner, "<[^>]+>", " ")
            inner = HtmlDecode(inner)
            inner = System.Text.RegularExpressions.Regex.Replace(inner, "\s+", " ").Trim()
            Return inner
        End Function


    Dim FormatSignatureDate As Func(Of String, String) =
        Function(raw As String) As String
            Dim s As String = If(raw, "").Trim()
            If String.IsNullOrWhiteSpace(s) Then
                Return ""
            End If
            Dim dt As System.DateTime
            If System.DateTime.TryParse(
                   s,
                   System.Globalization.CultureInfo.InvariantCulture,
                   System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                   dt
               ) Then
                Return dt.ToString("MM/dd/yyyy")
            End If
            If System.DateTime.TryParse(s, dt) Then
                Return dt.ToString("MM/dd/yyyy")
            End If
            Return ""
        End Function


    If String.IsNullOrWhiteSpace(pocId) Then
        Console.WriteLine("ABORT: planOfCareId is missing.")
        Throw New System.Exception("planOfCareId is required.")
    End If
    If String.IsNullOrWhiteSpace(assessId) Then
        Console.WriteLine("ABORT: assessmentId is missing.")
        Throw New System.Exception("assessmentId is required.")
    End If

    Dim pageUrl As String =
        NormalizeUrl(
            "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/CMIS/CMIS/EditProviderPlanOfCare/" &
            pocId &
            "?AssessmentId=" & System.Uri.EscapeDataString(assessId)
        )

    Console.WriteLine("GET EditProviderPlanOfCare | planOfCareId=" & pocId)

    Dim req As System.Net.HttpWebRequest =
        CType(System.Net.WebRequest.Create(pageUrl), System.Net.HttpWebRequest)

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
    req.Referer =
        "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/CMIS/CMIS/ProvidersIndex"
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
        If response.GetResponseStream() IsNot Nothing Then
            Using reader As New System.IO.StreamReader(response.GetResponseStream())
                responseHtml = reader.ReadToEnd()
            End Using
        End If
        finalUrl = NormalizeUrl(response.ResponseUri.ToString())
        statusCode = CInt(response.StatusCode)
    End Using

    Console.WriteLine(
        "GET -> HTTP " & statusCode.ToString() &
        " | bodyChars=" & responseHtml.Length.ToString()
    )

    If statusCode <> 200 Then
        Throw New System.Exception(
            "EditProviderPlanOfCare GET failed. HTTP " & statusCode.ToString()
        )
    End If

    If finalUrl.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
       finalUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 Then
        Throw New System.Exception(
            "Session expired — GET redirected to login."
        )
    End If

    If responseHtml.IndexOf("ccudate", StringComparison.OrdinalIgnoreCase) < 0 AndAlso
       responseHtml.IndexOf("CareCoordinator", StringComparison.OrdinalIgnoreCase) < 0 Then
        Throw New System.Exception(
            "Page HTML did not contain Care Coordinator signature fields."
        )
    End If

    Dim ccuDateHidden As String = GetInputValueById(responseHtml, "ccudate")
    Dim displayDate As String = GetSpanInnerText(responseHtml, "ccudateDisplay")

    careCoordinatorSignatureDate = FormatSignatureDate(ccuDateHidden)
    If String.IsNullOrWhiteSpace(careCoordinatorSignatureDate) Then
        careCoordinatorSignatureDate = FormatSignatureDate(displayDate)
    End If

    If String.IsNullOrWhiteSpace(careCoordinatorSignatureDate) Then
        Console.WriteLine("ABORT: Care Coordinator signature date not found on POC.")
        Throw New System.Exception(
            "Care Coordinator signature date is missing on EditProviderPlanOfCare " &
            "(planOfCareId=" & pocId & ", assessmentId=" & assessId & "). " &
            "The ccudate field and ccudateDisplay are empty or not a valid date."
        )
    End If

    careCoordinatorName = GetInputValueById(responseHtml, "CareCoordinator")

    Console.WriteLine(
        "Extracted | date=" & careCoordinatorSignatureDate &
        " | coordinator=" & careCoordinatorName
    )

    success = True
    Console.WriteLine("=== Get Care Coordinator Signature completed ===")

Catch ex As Exception

    success = False
    errorMessage = ex.ToString()
    Console.WriteLine("=== Get Care Coordinator Signature FAILED ===")
    Console.WriteLine(
        "At failure: HTTP=" & statusCode.ToString() &
        " | htmlChars=" & responseHtml.Length.ToString()
    )
    Console.WriteLine(errorMessage)

End Try
