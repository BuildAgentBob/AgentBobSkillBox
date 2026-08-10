' ============================================================
' ROCKAE LOGIN - UiPath Invoke Code (VB.NET)
'
' Marked action: Login — Log in to Rockae web app
'
' IN:
'   in_Email           As String
'   in_Password        As String
'
' OUT:
'   out_Session        As System.Collections.Generic.Dictionary(Of String, Object)
'   out_errorMessage   As String
' ============================================================

Try
    out_errorMessage = ""
    out_Session = New System.Collections.Generic.Dictionary(Of String, Object)()

    If String.IsNullOrWhiteSpace(in_Email) Then
        Throw New System.Exception("in_Email is required.")
    End If

    If String.IsNullOrWhiteSpace(in_Password) Then
        Throw New System.Exception("in_Password is required.")
    End If

    Dim baseUrl As String = "https://rockae.com"
    Dim loginUrl As String = baseUrl & "/api/auth/login"
    Dim cookieJar As New System.Net.CookieContainer()

    Console.WriteLine("Starting marked action: Login")
    Console.WriteLine("POST /api/auth/login")

    Dim payload As String =
        "{""email"":" & Newtonsoft.Json.JsonConvert.SerializeObject(in_Email) &
        ",""password"":" & Newtonsoft.Json.JsonConvert.SerializeObject(in_Password) & "}"

    Dim bytes As Byte() = System.Text.Encoding.UTF8.GetBytes(payload)

    Dim request = CType(System.Net.WebRequest.Create(loginUrl), System.Net.HttpWebRequest)
    request.Method = "POST"
    request.CookieContainer = cookieJar
    request.ContentType = "application/json"
    request.Accept = "*/*"
    request.UserAgent = "Mozilla/5.0"
    request.Headers.Add("Origin", baseUrl)
    request.Referer = baseUrl & "/login"
    request.ContentLength = bytes.Length
    request.AllowAutoRedirect = False

    Using reqStream = request.GetRequestStream()
        reqStream.Write(bytes, 0, bytes.Length)
    End Using

    Dim statusCode As Integer

    Try
        Using response = CType(request.GetResponse(), System.Net.HttpWebResponse)
            statusCode = CInt(response.StatusCode)
            Using sr As New System.IO.StreamReader(response.GetResponseStream())
                sr.ReadToEnd()
            End Using
        End Using
    Catch webEx As System.Net.WebException
        Dim httpResponse = TryCast(webEx.Response, System.Net.HttpWebResponse)
        If httpResponse Is Nothing Then
            Throw
        End If

        statusCode = CInt(httpResponse.StatusCode)
        Using sr As New System.IO.StreamReader(httpResponse.GetResponseStream())
            sr.ReadToEnd()
        End Using

        Throw New System.Exception("Rockae login failed with HTTP " & statusCode.ToString() & ".")
    End Try

    If statusCode < 200 OrElse statusCode >= 300 Then
        Throw New System.Exception("Rockae login failed with HTTP " & statusCode.ToString() & ".")
    End If

    Dim accessCookie = cookieJar.GetCookies(New System.Uri(baseUrl))("rockaeUserAccessToken")
    Dim refreshCookie = cookieJar.GetCookies(New System.Uri(baseUrl))("rockaeUserRefreshToken")

    If accessCookie Is Nothing OrElse String.IsNullOrWhiteSpace(accessCookie.Value) Then
        Throw New System.Exception("Rockae login did not return rockaeUserAccessToken cookie.")
    End If

    Console.WriteLine("Authentication cookies received.")

    out_Session("cookieContainer") = cookieJar
    out_Session("baseUrl") = baseUrl
    out_Session("IsLoggedIn") = True
    out_Session("hasAccessTokenCookie") = True
    out_Session("hasRefreshTokenCookie") = (refreshCookie IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(refreshCookie.Value))

    Console.WriteLine("Login completed successfully.")

Catch ex As System.Exception
    out_errorMessage = ex.Message
    Console.WriteLine("Rockae login failed.")
End Try
