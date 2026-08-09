Try

    errorMessage = ""
    Dim loginResponseHtml As String = ""

    cookies = New CookieContainer()
    sessionData = New Dictionary(Of String, Object)

    Dim baseUrl As String = "https://us.sandata.com"
    Dim loginUrl As String = $"{baseUrl}/security/login.aspx?agencyno={agencyNo}"

    Console.WriteLine("Loading Sandata login page...")

    '=========================================================
    ' GET LOGIN PAGE
    '=========================================================

    Dim request = CType(WebRequest.Create(loginUrl), HttpWebRequest)

    request.Method = "GET"
    request.CookieContainer = cookies
    request.AllowAutoRedirect = True
    request.UserAgent = "Mozilla/5.0"
    request.Accept = "text/html,application/xhtml+xml,*/*"

    Dim loginHtml As String

    Using response = CType(request.GetResponse(), HttpWebResponse)
        Using sr As New StreamReader(response.GetResponseStream())
            loginHtml = sr.ReadToEnd()
        End Using
    End Using

    Console.WriteLine("Extracting required hidden fields from the login page...")

    Dim GetHiddenValue As Func(Of String, String) =
        Function(fieldName As String)

            Dim m = Regex.Match(
                loginHtml,
                $"name=""{Regex.Escape(fieldName)}""[^>]*value=""([^""]*)""",
                RegexOptions.IgnoreCase Or RegexOptions.Singleline)

            If m.Success Then
                Return System.Net.WebUtility.HtmlDecode(m.Groups(1).Value)
            End If

            Return ""

        End Function

    Dim viewState As String = GetHiddenValue("__VIEWSTATE")
    Dim viewStateGenerator As String = GetHiddenValue("__VIEWSTATEGENERATOR")
    Dim eventValidation As String = GetHiddenValue("__EVENTVALIDATION")
    Dim hdnClose As String = GetHiddenValue("hdnClose")
    Dim hdnID As String = GetHiddenValue("hdnID")

    If String.IsNullOrWhiteSpace(viewState) Then
        Throw New Exception("Unable to locate __VIEWSTATE.")
    End If

    If String.IsNullOrWhiteSpace(eventValidation) Then
        Throw New Exception("Unable to locate __EVENTVALIDATION.")
    End If

    Console.WriteLine("Required hidden fields were extracted from the login page.")

    sessionData("__VIEWSTATE") = viewState
    sessionData("__VIEWSTATEGENERATOR") = viewStateGenerator
    sessionData("__EVENTVALIDATION") = eventValidation
    sessionData("hdnClose") = hdnClose
    sessionData("hdnID") = hdnID

    Console.WriteLine("Saving login page values for later Sandata requests...")

    Dim postData As String =
        "__EVENTTARGET=lnkSubmit" &
        "&__EVENTARGUMENT=" &
        "&__VIEWSTATE=" & Uri.EscapeDataString(viewState) &
        "&__VIEWSTATEGENERATOR=" & Uri.EscapeDataString(viewStateGenerator) &
        "&__EVENTVALIDATION=" & Uri.EscapeDataString(eventValidation) &
        "&txtUserName=" & Uri.EscapeDataString(username) &
        "&txtPassword=" & Uri.EscapeDataString(password) &
        "&hdnClose=" & Uri.EscapeDataString(If(String.IsNullOrWhiteSpace(hdnClose), "0", hdnClose)) &
        "&hdnID=" & Uri.EscapeDataString(hdnID)

    Dim bytes = Encoding.UTF8.GetBytes(postData)

    Console.WriteLine("Submitting Sandata login credentials...")

    Dim postRequest = CType(WebRequest.Create(loginUrl), HttpWebRequest)

    postRequest.Method = "POST"
    postRequest.CookieContainer = cookies
    postRequest.AllowAutoRedirect = False
    postRequest.ContentType = "application/x-www-form-urlencoded"
    postRequest.ContentLength = bytes.Length
    postRequest.UserAgent = "Mozilla/5.0"
    postRequest.Referer = loginUrl

    Using stream = postRequest.GetRequestStream()
        stream.Write(bytes, 0, bytes.Length)
    End Using

    Dim redirectUrl As String = ""

    Using response = CType(postRequest.GetResponse(), HttpWebResponse)

        If response.StatusCode = HttpStatusCode.Found OrElse
           response.StatusCode = HttpStatusCode.Redirect Then

            redirectUrl = response.Headers("Location")

        Else

            Using sr As New StreamReader(response.GetResponseStream())
                loginResponseHtml = sr.ReadToEnd()
            End Using

        End If

    End Using

    If String.IsNullOrWhiteSpace(redirectUrl) Then
        Throw New Exception("Login failed. Sandata did not return a redirect.")
    End If

    If redirectUrl.StartsWith("/") Then
        redirectUrl = baseUrl & redirectUrl
    End If

    Console.WriteLine($"Sandata returned login redirect: {redirectUrl}")

    sessionData("HomeUrl") = redirectUrl

    Console.WriteLine("Loading Sandata home page to confirm login...")

    Dim homeRequest = CType(WebRequest.Create(redirectUrl), HttpWebRequest)

    homeRequest.Method = "GET"
    homeRequest.CookieContainer = cookies
    homeRequest.UserAgent = "Mozilla/5.0"
    homeRequest.Referer = loginUrl

    Using response = CType(homeRequest.GetResponse(), HttpWebResponse)

        Using sr As New StreamReader(response.GetResponseStream())
            loginResponseHtml = sr.ReadToEnd()
        End Using

    End Using

    Console.WriteLine("Checking Sandata home page response to confirm login...")

    If loginResponseHtml.Contains("Sandata") AndAlso
       loginResponseHtml.Contains("Help at Home") Then

        Console.WriteLine("Successfully logged in to Sandata.")

    Else

        Throw New Exception("Unable to verify Sandata login from the home page response.")

    End If

    sessionData("IsLoggedIn") = True

    Console.WriteLine("Sandata session is ready for subsequent requests.")

Catch ex As Exception

    errorMessage = ex.Message
    Console.WriteLine($"Sandata login failed: {errorMessage}")

End Try