' ============================================================
' ROCKAE LOGIN
'
' IN:
'   in_Email             As String
'   in_Password          As String
'
' OUT:
'   out_CookieContainer  As System.Net.CookieContainer
'   out_Session          As Dictionary(Of String, Object)
'   out_errorMessage     As String
' ============================================================

out_errorMessage = String.Empty
out_Session = New System.Collections.Generic.Dictionary(Of String, Object)()
out_CookieContainer = Nothing

Try

    Console.WriteLine("Starting marked action: Login")

    Dim baseUri As New System.Uri("https://rockae.com/")
    Dim loginUri As New System.Uri(baseUri, "api/auth/login")

    Dim cookieJar As New System.Net.CookieContainer()

    Dim handler As New System.Net.Http.HttpClientHandler()
    handler.UseCookies = True
    handler.CookieContainer = cookieJar
    handler.AllowAutoRedirect = True

    Using client As New System.Net.Http.HttpClient(handler)

        client.Timeout = System.TimeSpan.FromSeconds(60)

        Dim loginData As New System.Collections.Generic.Dictionary(Of String, String)
        loginData("email") = in_Email
        loginData("password") = in_Password

        Dim loginJson As String =
            Newtonsoft.Json.JsonConvert.SerializeObject(loginData)

        Console.WriteLine("POST /api/auth/login")

        Dim request As New System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            loginUri
        )

        request.Headers.TryAddWithoutValidation("Accept", "*/*")
        request.Headers.TryAddWithoutValidation("Origin", "https://rockae.com")
        request.Headers.Referrer = New System.Uri("https://rockae.com/login")

        request.Content =
            New System.Net.Http.StringContent(
                loginJson,
                System.Text.Encoding.UTF8,
                "application/json"
            )

        Using response As System.Net.Http.HttpResponseMessage =
            client.SendAsync(request).GetAwaiter().GetResult()

            Dim responseBody As String =
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

            If Not response.IsSuccessStatusCode Then
                Throw New System.Exception(
                    "Rockae login failed. HTTP " &
                    CInt(response.StatusCode).ToString() & "."
                )
            End If

        End Using

        Dim cookies As System.Net.CookieCollection = cookieJar.GetCookies(baseUri)
        Dim hasAccess As Boolean = False
        Dim hasRefresh As Boolean = False

        For Each currentCookie As System.Net.Cookie In cookies
            If currentCookie.Name.Equals(
                "rockaeUserAccessToken",
                System.StringComparison.OrdinalIgnoreCase
            ) AndAlso Not String.IsNullOrWhiteSpace(currentCookie.Value) Then
                hasAccess = True
            ElseIf currentCookie.Name.Equals(
                "rockaeUserRefreshToken",
                System.StringComparison.OrdinalIgnoreCase
            ) AndAlso Not String.IsNullOrWhiteSpace(currentCookie.Value) Then
                hasRefresh = True
            End If
        Next

        If Not hasAccess Then
            Throw New System.Exception(
                "Rockae login succeeded but rockaeUserAccessToken cookie was not returned."
            )
        End If

        If Not hasRefresh Then
            Throw New System.Exception(
                "Rockae login succeeded but rockaeUserRefreshToken cookie was not returned."
            )
        End If

        Console.WriteLine("Authentication cookies received.")

        ' Cookie jar is the auth handoff — not stored inside out_Session.
        out_CookieContainer = cookieJar

        ' Session bag = optional non-cookie extras for later steps.
        out_Session("baseUrl") = "https://rockae.com"
        out_Session("IsLoggedIn") = True

        Console.WriteLine("Login completed successfully.")

    End Using

Catch ex As System.Exception

    out_errorMessage = ex.Message
    Console.WriteLine("Rockae login failed.")

End Try
