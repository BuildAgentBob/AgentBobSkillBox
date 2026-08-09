Try
    errorMessage = Nothing
    oktaRedirectLocation = Nothing
    isAuthenticated = False

    cookies = New System.Net.CookieContainer()

    Dim cookieJar As System.Net.CookieContainer = cookies

    Dim oktaBaseUrl As String = "https://helpathome.okta.com"
    Dim centralUrl As String =
        "https://central.axxessweb.com/?login_hint=helpathome.com"

    Dim clientId As String =
        "okta.2b1959c8-bcc0-56eb-a589-cfcfb7422f26"

    Dim redirectUri As String =
        "https://helpathome.okta.com/enduser/callback"

    Dim scope As String =
        "openid profile email " &
        "okta.users.read.self " &
        "okta.users.manage.self " &
        "okta.internal.enduser.read " &
        "okta.internal.enduser.manage " &
        "okta.enduser.dashboard.read " &
        "okta.enduser.dashboard.manage " &
        "okta.myAccount.sessions.manage " &
        "okta.internal.navigation.enduser.read"

    ' =====================================================
    ' 1. GENERATE PKCE VALUES
    ' =====================================================

    Dim randomBytes(31) As Byte

    Using rng =
        System.Security.Cryptography.RandomNumberGenerator.Create()

        rng.GetBytes(randomBytes)
    End Using

    Dim codeVerifier As String =
        Convert.ToBase64String(randomBytes).
            TrimEnd("="c).
            Replace("+"c, "-"c).
            Replace("/"c, "_"c)

    Dim challengeBytes As Byte()

    Using sha256 =
        System.Security.Cryptography.SHA256.Create()

        challengeBytes =
            sha256.ComputeHash(
                System.Text.Encoding.ASCII.GetBytes(codeVerifier))
    End Using

    Dim codeChallenge As String =
        Convert.ToBase64String(challengeBytes).
            TrimEnd("="c).
            Replace("+"c, "-"c).
            Replace("/"c, "_"c)

    Dim state As String =
        Guid.NewGuid().ToString("N") &
        Guid.NewGuid().ToString("N")

    Dim nonce As String =
        Guid.NewGuid().ToString("N") &
        Guid.NewGuid().ToString("N")

    ' =====================================================
    ' 2. OPEN OKTA DASHBOARD AUTHORIZE URL
    ' =====================================================

    Dim authorizeUrl As String =
        oktaBaseUrl & "/oauth2/v1/authorize" &
        "?client_id=" & Uri.EscapeDataString(clientId) &
        "&code_challenge=" & Uri.EscapeDataString(codeChallenge) &
        "&code_challenge_method=S256" &
        "&nonce=" & Uri.EscapeDataString(nonce) &
        "&redirect_uri=" & Uri.EscapeDataString(redirectUri) &
        "&response_type=code" &
        "&state=" & Uri.EscapeDataString(state) &
        "&scope=" & Uri.EscapeDataString(scope)

    Console.WriteLine("Opening Okta Dashboard authorization page...")

    Dim authorizeRequest =
        CType(
            System.Net.WebRequest.Create(authorizeUrl),
            System.Net.HttpWebRequest)

    authorizeRequest.Method = "GET"
    authorizeRequest.CookieContainer = cookieJar
    authorizeRequest.AllowAutoRedirect = True
    authorizeRequest.Accept =
        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
    authorizeRequest.UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
        "AppleWebKit/537.36 (KHTML, like Gecko) " &
        "Chrome/150.0.0.0 Safari/537.36"

    authorizeRequest.Headers.Add("Cache-Control", "no-cache")
    authorizeRequest.Headers.Add("Pragma", "no-cache")

    Dim authorizeHtml As String = Nothing

    Using authorizeResponse =
        CType(
            authorizeRequest.GetResponse(),
            System.Net.HttpWebResponse)

        Using reader As New System.IO.StreamReader(
            authorizeResponse.GetResponseStream())

            authorizeHtml = reader.ReadToEnd()
        End Using
    End Using

    If String.IsNullOrWhiteSpace(authorizeHtml) Then
        Throw New Exception(
            "Okta authorize response was empty.")
    End If

    ' =====================================================
    ' 3. EXTRACT stateToken FROM AUTHORIZE HTML
    ' =====================================================

    Dim stateTokenMatch =
        System.Text.RegularExpressions.Regex.Match(
            authorizeHtml,
            "var\s+stateToken\s*=\s*['""]([^'""]+)['""]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)

    If Not stateTokenMatch.Success Then
        Throw New Exception(
            "Could not find Okta stateToken in the authorize response.")
    End If

    Dim stateToken As String =
        stateTokenMatch.Groups(1).Value

    ' Decode JavaScript hexadecimal escapes used by Okta.
    stateToken =
        System.Text.RegularExpressions.Regex.Replace(
            stateToken,
            "\\x([0-9A-Fa-f]{2})",
            Function(m) ChrW(Convert.ToInt32(m.Groups(1).Value, 16)))

    If String.IsNullOrWhiteSpace(stateToken) Then
        Throw New Exception(
            "Extracted Okta stateToken was empty.")
    End If

    Console.WriteLine("Okta stateToken extracted.")

    ' =====================================================
    ' 4. CALL IDX INTROSPECT
    ' =====================================================

    Dim introspectPayload As String =
        Newtonsoft.Json.JsonConvert.SerializeObject(
            New With {
                .stateToken = stateToken
            })

    Dim introspectRequest =
        CType(
            System.Net.WebRequest.Create(
                oktaBaseUrl & "/idp/idx/introspect"),
            System.Net.HttpWebRequest)

    introspectRequest.Method = "POST"
    introspectRequest.CookieContainer = cookieJar
    introspectRequest.ContentType =
        "application/ion+json; okta-version=1.0.0"
    introspectRequest.Accept =
        "application/ion+json; okta-version=1.0.0"
    introspectRequest.UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
        "AppleWebKit/537.36 (KHTML, like Gecko) " &
        "Chrome/150.0.0.0 Safari/537.36"

    introspectRequest.Referer = authorizeUrl
    introspectRequest.Headers.Add("Origin", oktaBaseUrl)
    introspectRequest.Headers.Add(
        "x-okta-user-agent-extended",
        "okta-auth-js/7.14.5 " &
        "okta-signin-widget-7.47.2 okta-hosted")

    introspectRequest.Headers.Add(
        "Cache-Control",
        "no-cache")

    introspectRequest.Headers.Add(
        "Pragma",
        "no-cache")

    Dim introspectBytes =
        System.Text.Encoding.UTF8.GetBytes(
            introspectPayload)

    introspectRequest.ContentLength =
        introspectBytes.Length

    Using stream =
        introspectRequest.GetRequestStream()

        stream.Write(
            introspectBytes,
            0,
            introspectBytes.Length)
    End Using

    Dim introspectResponseText As String = Nothing

    Using introspectResponse =
        CType(
            introspectRequest.GetResponse(),
            System.Net.HttpWebResponse)

        Using reader As New System.IO.StreamReader(
            introspectResponse.GetResponseStream())

            introspectResponseText =
                reader.ReadToEnd()
        End Using
    End Using

    If String.IsNullOrWhiteSpace(
        introspectResponseText) Then

        Throw New Exception(
            "Okta introspect response was empty.")
    End If

    Dim introspectJson =
        Newtonsoft.Json.Linq.JObject.Parse(
            introspectResponseText)

    Dim stateHandle As String = Nothing

    If introspectJson("stateHandle") IsNot Nothing Then
        stateHandle =
            introspectJson("stateHandle").ToString()
    End If

    If String.IsNullOrWhiteSpace(stateHandle) Then

        Dim stateHandleToken =
            introspectJson.SelectToken(
                "remediation.value[?(@.name == 'identify')]" &
                ".value[?(@.name == 'stateHandle')].value")

        If stateHandleToken IsNot Nothing Then
            stateHandle =
                stateHandleToken.ToString()
        End If
    End If

    If String.IsNullOrWhiteSpace(stateHandle) Then
        Throw New Exception(
            "Okta introspect response did not contain stateHandle.")
    End If

    Console.WriteLine(
        "Okta introspect completed.")

    ' =====================================================
    ' 5. SUBMIT USERNAME
    ' =====================================================

    Dim identifyPayload As String =
        Newtonsoft.Json.JsonConvert.SerializeObject(
            New With {
                .identifier = username,
                .stateHandle = stateHandle
            })

    Dim identifyRequest =
        CType(
            System.Net.WebRequest.Create(
                oktaBaseUrl & "/idp/idx/identify"),
            System.Net.HttpWebRequest)

    identifyRequest.Method = "POST"
    identifyRequest.CookieContainer = cookieJar
    identifyRequest.ContentType = "application/json"
    identifyRequest.Accept =
        "application/json; okta-version=1.0.0"
    identifyRequest.UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
        "AppleWebKit/537.36 (KHTML, like Gecko) " &
        "Chrome/150.0.0.0 Safari/537.36"

    identifyRequest.Referer = authorizeUrl
    identifyRequest.Headers.Add("Origin", oktaBaseUrl)
    identifyRequest.Headers.Add(
        "x-okta-user-agent-extended",
        "okta-auth-js/7.14.5 " &
        "okta-signin-widget-7.47.2 okta-hosted")

    Dim identifyBytes =
        System.Text.Encoding.UTF8.GetBytes(
            identifyPayload)

    identifyRequest.ContentLength =
        identifyBytes.Length

    Using stream =
        identifyRequest.GetRequestStream()

        stream.Write(
            identifyBytes,
            0,
            identifyBytes.Length)
    End Using

    Dim identifyResponseText As String = Nothing

    Using identifyResponse =
        CType(
            identifyRequest.GetResponse(),
            System.Net.HttpWebResponse)

        Using reader As New System.IO.StreamReader(
            identifyResponse.GetResponseStream())

            identifyResponseText =
                reader.ReadToEnd()
        End Using
    End Using

    If String.IsNullOrWhiteSpace(
        identifyResponseText) Then

        Throw New Exception(
            "Okta identify response was empty.")
    End If

    Dim identifyJson =
        Newtonsoft.Json.Linq.JObject.Parse(
            identifyResponseText)

    ' Check whether Okta returned an error.
    If identifyJson("messages") IsNot Nothing Then

        Dim messageToken =
            identifyJson.SelectToken(
                "messages.value[0].message")

        If messageToken IsNot Nothing Then
            Throw New Exception(
                "Okta identify failed: " &
                messageToken.ToString())
        End If
    End If

    Dim passwordRemediation As Newtonsoft.Json.Linq.JToken = Nothing

    Dim remediationItems =
        TryCast(
            identifyJson.SelectToken(
                "remediation.value"),
            Newtonsoft.Json.Linq.JArray)

    If remediationItems Is Nothing Then
        Throw New Exception(
            "Okta identify response did not contain remediation.value.")
    End If

    For Each item As Newtonsoft.Json.Linq.JToken In remediationItems

        If item("name") IsNot Nothing AndAlso
           item("name").ToString() =
               "challenge-authenticator" Then

            passwordRemediation = item
            Exit For
        End If
    Next

    If passwordRemediation Is Nothing Then

        Dim selectAuthenticator =
            remediationItems.FirstOrDefault(
                Function(x) _
                    x("name") IsNot Nothing AndAlso
                    x("name").ToString() =
                        "select-authenticator-authenticate")

        If selectAuthenticator IsNot Nothing Then
            Throw New Exception(
                "Okta requires an authenticator selection " &
                "before the password can be submitted.")
        End If

        Throw New Exception(
            "Okta did not return the password challenge remediation.")
    End If

    Dim challengeValues =
        TryCast(
            passwordRemediation("value"),
            Newtonsoft.Json.Linq.JArray)

    If challengeValues Is Nothing Then
        Throw New Exception(
            "Password challenge did not contain a value array.")
    End If

    stateHandle = Nothing

    For Each valueItem As Newtonsoft.Json.Linq.JToken In challengeValues

        If valueItem("name") IsNot Nothing AndAlso
           valueItem("name").ToString() =
               "stateHandle" AndAlso
           valueItem("value") IsNot Nothing Then

            stateHandle =
                valueItem("value").ToString()

            Exit For
        End If
    Next

    If String.IsNullOrWhiteSpace(stateHandle) Then
        Throw New Exception(
            "Okta password challenge did not contain stateHandle.")
    End If

    Console.WriteLine(
        "Okta username accepted.")

    ' =====================================================
    ' 6. SUBMIT PASSWORD
    ' =====================================================

    Dim passwordPayload As String =
        Newtonsoft.Json.JsonConvert.SerializeObject(
            New With {
                .credentials = New With {
                    .passcode = password
                },
                .stateHandle = stateHandle
            })

    Dim passwordRequest =
        CType(
            System.Net.WebRequest.Create(
                oktaBaseUrl &
                "/idp/idx/challenge/answer"),
            System.Net.HttpWebRequest)

    passwordRequest.Method = "POST"
    passwordRequest.CookieContainer = cookieJar
    passwordRequest.ContentType = "application/json"
    passwordRequest.Accept =
        "application/json; okta-version=1.0.0"
    passwordRequest.UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
        "AppleWebKit/537.36 (KHTML, like Gecko) " &
        "Chrome/150.0.0.0 Safari/537.36"

    passwordRequest.Referer = authorizeUrl
    passwordRequest.Headers.Add(
        "Origin",
        oktaBaseUrl)

    passwordRequest.Headers.Add(
        "x-okta-user-agent-extended",
        "okta-auth-js/7.14.5 " &
        "okta-signin-widget-7.47.2 okta-hosted")

    Dim passwordBytes =
        System.Text.Encoding.UTF8.GetBytes(
            passwordPayload)

    passwordRequest.ContentLength =
        passwordBytes.Length

    Using stream =
        passwordRequest.GetRequestStream()

        stream.Write(
            passwordBytes,
            0,
            passwordBytes.Length)
    End Using

    Dim passwordResponseText As String = Nothing

    Using passwordResponse =
        CType(
            passwordRequest.GetResponse(),
            System.Net.HttpWebResponse)

        Using reader As New System.IO.StreamReader(
            passwordResponse.GetResponseStream())

            passwordResponseText =
                reader.ReadToEnd()
        End Using
    End Using

    If String.IsNullOrWhiteSpace(
        passwordResponseText) Then

        Throw New Exception(
            "Okta password response was empty.")
    End If

    Dim passwordJson =
        Newtonsoft.Json.Linq.JObject.Parse(
            passwordResponseText)

    If passwordJson("messages") IsNot Nothing Then

        Dim messageToken =
            passwordJson.SelectToken(
                "messages.value[0].message")

        If messageToken IsNot Nothing Then
            Throw New Exception(
                "Okta authentication failed: " &
                messageToken.ToString())
        End If
    End If

    Dim successUrl As String = Nothing

    If passwordJson("success") IsNot Nothing AndAlso
       passwordJson("success")("href") IsNot Nothing Then

        successUrl =
            passwordJson("success")("href").ToString()
    End If

    If String.IsNullOrWhiteSpace(successUrl) Then

        Dim currentAuthenticator =
            passwordJson.SelectToken(
                "currentAuthenticator.value.displayName")

        If currentAuthenticator IsNot Nothing Then
            Throw New Exception(
                "Okta requires another authentication step: " &
                currentAuthenticator.ToString())
        End If

        Throw New Exception(
            "Okta did not return success.href after password submission.")
    End If

    Console.WriteLine(
        "Okta password accepted.")

    ' =====================================================
    ' 7. FOLLOW SUCCESS URL TO ESTABLISH OKTA SESSION
    ' =====================================================

    Dim successRequest =
        CType(
            System.Net.WebRequest.Create(successUrl),
            System.Net.HttpWebRequest)

    successRequest.Method = "GET"
    successRequest.CookieContainer = cookieJar
    successRequest.AllowAutoRedirect = True
    successRequest.Accept =
        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
    successRequest.UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
        "AppleWebKit/537.36 (KHTML, like Gecko) " &
        "Chrome/150.0.0.0 Safari/537.36"

    Dim successFinalUrl As String = Nothing

    Using successResponse =
        CType(
            successRequest.GetResponse(),
            System.Net.HttpWebResponse)

        successFinalUrl =
            successResponse.ResponseUri.AbsoluteUri

        If successResponse.GetResponseStream() IsNot Nothing Then
            Using reader As New System.IO.StreamReader(
                successResponse.GetResponseStream())

                reader.ReadToEnd()
            End Using
        End If
    End Using

    Console.WriteLine(
        "Okta session establishment completed.")

  ' =====================================================
' 8. LAUNCH AXXESS THROUGH OKTA DASHBOARD BOOKMARK
' =====================================================

Console.WriteLine("Launching Axxess from Okta dashboard...")

Dim axxessBookmarkUrl As String =
    "https://helpathome.okta.com/home/bookmark/0oah0aiotaDJcHgEt697/2557"

Dim currentUrl As String = axxessBookmarkUrl
Dim redirectCount As Integer = 0
Dim maxRedirects As Integer = 20

Do While redirectCount < maxRedirects

    Dim req =
        CType(
            System.Net.WebRequest.Create(currentUrl),
            System.Net.HttpWebRequest)

    req.Method = "GET"
    req.CookieContainer = cookieJar
    req.AllowAutoRedirect = False

    req.Accept =
        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"

    req.UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
        "AppleWebKit/537.36 (KHTML, like Gecko) " &
        "Chrome/149.0.0.0 Safari/537.36"

    req.Referer =
        "https://helpathome.okta.com/app/UserHome"

    Dim statusCode As Integer = 0
    Dim location As String = Nothing
    Dim responseBody As String = Nothing

    Try

        Using resp =
            CType(
                req.GetResponse(),
                System.Net.HttpWebResponse)

            statusCode =
                CInt(resp.StatusCode)

            location =
                resp.Headers("Location")

            If resp.GetResponseStream() IsNot Nothing Then

                Using reader As New System.IO.StreamReader(
                    resp.GetResponseStream())

                    responseBody =
                        reader.ReadToEnd()

                End Using

            End If

        End Using

    Catch webEx As System.Net.WebException

        If webEx.Response Is Nothing Then
            Throw
        End If

        Using resp =
            CType(
                webEx.Response,
                System.Net.HttpWebResponse)

            statusCode =
                CInt(resp.StatusCode)

            location =
                resp.Headers("Location")

            If resp.GetResponseStream() IsNot Nothing Then

                Using reader As New System.IO.StreamReader(
                    resp.GetResponseStream())

                    responseBody =
                        reader.ReadToEnd()

                End Using

            End If

        End Using

    End Try

    Console.WriteLine(
        "Axxess launch step " &
        redirectCount.ToString() &
        ": HTTP " &
        statusCode.ToString())

    If Not String.IsNullOrWhiteSpace(location) Then

        Dim nextUrl As String

        If Uri.IsWellFormedUriString(
            location,
            UriKind.Absolute) Then

            ' Prefer raw absolute Location to preserve fragments.
            nextUrl = location

        Else

            Dim resolvedUri As New Uri(
                New Uri(currentUrl),
                location)

            Dim fragmentFromLocation As String = Nothing
            Dim hashIndex As Integer =
                location.IndexOf("#"c)

            If hashIndex >= 0 Then
                fragmentFromLocation =
                    location.Substring(hashIndex)
            End If

            If Not String.IsNullOrEmpty(fragmentFromLocation) AndAlso
               String.IsNullOrEmpty(resolvedUri.Fragment) Then

                nextUrl =
                    resolvedUri.GetLeftPart(UriPartial.Query) &
                    fragmentFromLocation
            ElseIf Not String.IsNullOrEmpty(resolvedUri.Fragment) Then
                nextUrl = resolvedUri.OriginalString
            Else
                nextUrl = resolvedUri.AbsoluteUri
            End If

        End If

        Console.WriteLine(
            "Axxess redirect host: " &
            New Uri(nextUrl).Host)

        ' Capture Axxess/Okta OAuth callback fragments for Finalize.
        If nextUrl.Contains("#") AndAlso
           nextUrl.Contains("code=") AndAlso
           nextUrl.Contains("state=") Then

            oktaRedirectLocation = nextUrl

            Console.WriteLine(
                "Axxess callback with code/state captured successfully.")

            Exit Do
        End If

        currentUrl = nextUrl
        redirectCount += 1

        Continue Do

    End If

    If statusCode = 200 Then

        If currentUrl.Contains("#") AndAlso
           currentUrl.Contains("code=") AndAlso
           currentUrl.Contains("state=") Then

            oktaRedirectLocation = currentUrl

            Console.WriteLine(
                "Axxess callback with code/state captured successfully.")

            Exit Do
        End If

        oktaRedirectLocation =
            currentUrl

        Console.WriteLine(
            "Axxess launch finished at: " &
            currentUrl)

        Exit Do

    End If

    Throw New Exception(
        "Axxess launch stopped unexpectedly. HTTP " &
        statusCode.ToString() &
        " at " &
        currentUrl)

Loop

If redirectCount >= maxRedirects Then

    Throw New Exception(
        "Axxess launch exceeded maximum redirects.")

End If

If String.IsNullOrWhiteSpace(
    oktaRedirectLocation) Then

    Throw New Exception(
        "Axxess launch did not produce a final URL.")

End If

isAuthenticated = True

Console.WriteLine("Axxess Okta bookmark launch completed.")

Catch ex As System.Net.WebException

    If ex.Response IsNot Nothing Then

        Using response =
            CType(
                ex.Response,
                System.Net.HttpWebResponse)

            Dim responseText As String = Nothing

            If response.GetResponseStream() IsNot Nothing Then

                Using reader As New System.IO.StreamReader(
                    response.GetResponseStream())

                    responseText =
                        reader.ReadToEnd()
                End Using
            End If

            errorMessage =
                response.StatusCode.ToString() &
                ": " &
                If(responseText, "")
        End Using

    Else
        errorMessage = ex.Message
    End If

Catch ex As Exception
    errorMessage = ex.Message
End Try
