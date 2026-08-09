Try
    errorMessage = Nothing
    accessToken = Nothing
    buildDateIdentifier = Nothing
    identityAuthorizeLocation = Nothing
    axxessResponse = Nothing
    Dim agencyId As String = Nothing
    Dim userId As String = Nothing

    If cookies Is Nothing Then
        Throw New Exception(
            "The shared authentication CookieContainer is Nothing.")
    End If

    Dim browserUserAgent As String =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
        "AppleWebKit/537.36 (KHTML, like Gecko) " &
        "Chrome/150.0.0.0 Safari/537.36"

    ' Used by Identity SPA SSO (challenge/email). Pass the same username
    ' used for Okta, with or without @helpathome.com.
    Dim identityEmail As String = Nothing

    If Not String.IsNullOrWhiteSpace(username) Then
        If username.Contains("@") Then
            identityEmail = username.Trim()
        Else
            identityEmail = username.Trim() & "@helpathome.com"
        End If
    End If

    ' =====================================================
    ' 1. GENERATE AXXESS ACCESS TOKEN
    ' =====================================================

    Console.WriteLine("Generating Axxess access token...")

    Dim authorizeAttempt As Integer = 0
    Dim maximumAuthorizeAttempts As Integer = 2
    Dim identitySessionEstablished As Boolean = False
    Dim tokenFragmentCaptured As Boolean = False

    Do While authorizeAttempt < maximumAuthorizeAttempts AndAlso
             Not tokenFragmentCaptured

        authorizeAttempt += 1

        Dim state As String =
            Guid.NewGuid().ToString("N")

        Dim nonce As String =
            Guid.NewGuid().ToString("N")

        Dim authorizeUrl As String =
            "https://identity.axxessweb.com/connect/authorize" &
            "?client_id=Axxess-Router-Frontend" &
            "&redirect_uri=" &
                Uri.EscapeDataString(
                    "https://central.axxessweb.com/oidc/sign-in-redirect") &
            "&response_type=" &
                Uri.EscapeDataString("id_token token") &
            "&scope=" &
                Uri.EscapeDataString("openid profile email") &
            "&state=" &
                Uri.EscapeDataString(state) &
            "&nonce=" &
                Uri.EscapeDataString(nonce)

        Console.WriteLine(
            "Calling Axxess /connect/authorize (attempt " &
            authorizeAttempt.ToString() &
            ")...")

        Dim authorizeRequest =
            CType(
                System.Net.WebRequest.Create(authorizeUrl),
                System.Net.HttpWebRequest)

        authorizeRequest.Method = "GET"
        authorizeRequest.CookieContainer = cookies
        authorizeRequest.AllowAutoRedirect = False
        authorizeRequest.Accept =
            "text/html,application/xhtml+xml,application/xml;q=0.9," &
            "image/avif,image/webp,image/apng,*/*;q=0.8"
        authorizeRequest.UserAgent = browserUserAgent
        authorizeRequest.Referer =
            "https://central.axxessweb.com/"
        authorizeRequest.Headers.Add(
            "Cache-Control",
            "no-cache")
        authorizeRequest.Headers.Add(
            "Pragma",
            "no-cache")

        Dim authorizeStatusCode As Integer = 0
        Dim authorizeResponseBody As String = Nothing
        identityAuthorizeLocation = Nothing

        Try
            Using response =
                CType(
                    authorizeRequest.GetResponse(),
                    System.Net.HttpWebResponse)

                authorizeStatusCode =
                    CInt(response.StatusCode)

                identityAuthorizeLocation =
                    response.Headers("Location")

                If response.GetResponseStream() IsNot Nothing Then
                    Using reader As New System.IO.StreamReader(
                        response.GetResponseStream())

                        authorizeResponseBody =
                            reader.ReadToEnd()
                    End Using
                End If
            End Using

        Catch webEx As System.Net.WebException

            If webEx.Response Is Nothing Then
                Throw
            End If

            Using response =
                CType(
                    webEx.Response,
                    System.Net.HttpWebResponse)

                authorizeStatusCode =
                    CInt(response.StatusCode)

                identityAuthorizeLocation =
                    response.Headers("Location")

                If response.GetResponseStream() IsNot Nothing Then
                    Using reader As New System.IO.StreamReader(
                        response.GetResponseStream())

                        authorizeResponseBody =
                            reader.ReadToEnd()
                    End Using
                End If
            End Using
        End Try

        If String.IsNullOrWhiteSpace(
            identityAuthorizeLocation) Then

            Throw New Exception(
                "Authorize Location header was empty. HTTP status: " &
                authorizeStatusCode.ToString() &
                ". Response starts with: " &
                If(
                    String.IsNullOrWhiteSpace(authorizeResponseBody),
                    "",
                    authorizeResponseBody.Substring(
                        0,
                        Math.Min(
                            500,
                            authorizeResponseBody.Length))))
        End If

        ' Happy path: token fragment already returned.
        If identityAuthorizeLocation.Contains("#") AndAlso
           identityAuthorizeLocation.IndexOf(
               "access_token=",
               StringComparison.OrdinalIgnoreCase) >= 0 Then

            tokenFragmentCaptured = True
            Exit Do
        End If

        Dim needsIdentityLogin As Boolean =
            identityAuthorizeLocation.IndexOf(
                "/api/v1/auth/login",
                StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            identityAuthorizeLocation.IndexOf(
                "/login",
                StringComparison.OrdinalIgnoreCase) >= 0

        If Not needsIdentityLogin Then
            Throw New Exception(
                "Authorize Location does not contain a token fragment: " &
                identityAuthorizeLocation)
        End If

        If identitySessionEstablished Then
            Throw New Exception(
                "Axxess Identity session has not been established. " &
                "The authorize request still redirected to the Axxess login endpoint: " &
                identityAuthorizeLocation)
        End If

        Console.WriteLine(
            "Starting Axxess Identity login redirect chain...")

        Dim walkUrl As String = identityAuthorizeLocation

        If Not Uri.IsWellFormedUriString(
            walkUrl,
            UriKind.Absolute) Then

            walkUrl =
                New Uri(
                    New Uri("https://identity.axxessweb.com"),
                    walkUrl).AbsoluteUri
        End If

        Dim walkReferer As String =
            "https://central.axxessweb.com/"
        Dim walkHop As Integer = 0
        Dim maximumWalkHops As Integer = 25
        Dim visitedOkta As Boolean = False
        Dim ssoReturnUrl As String = Nothing
        Dim visitedWalkUrls As New System.Collections.Generic.HashSet(Of String)(
            StringComparer.OrdinalIgnoreCase)

        Do While walkHop < maximumWalkHops

            walkHop += 1

            Dim walkHost As String = Nothing
            Dim walkQuery As String = Nothing
            Dim walkFragment As String = Nothing
            Dim callbackParamSource As String = Nothing

            Try
                Dim walkUri As New Uri(walkUrl)
                walkHost = walkUri.Host
                walkQuery = walkUri.Query
                walkFragment = walkUri.Fragment
            Catch
                walkHost = Nothing
                walkQuery = Nothing
                walkFragment = Nothing
            End Try

            ' Okta returns code/state in the HASH fragment:
            ' https://identity.axxessweb.com/login#code=...&state=...
            If Not String.IsNullOrWhiteSpace(walkFragment) AndAlso
               walkFragment.IndexOf(
                   "code=",
                   StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
               walkFragment.IndexOf(
                   "state=",
                   StringComparison.OrdinalIgnoreCase) >= 0 Then

                callbackParamSource = walkFragment.TrimStart("#"c)

            ElseIf Not String.IsNullOrWhiteSpace(walkQuery) AndAlso
                   walkQuery.IndexOf(
                       "code=",
                       StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                   walkQuery.IndexOf(
                       "state=",
                       StringComparison.OrdinalIgnoreCase) >= 0 Then

                callbackParamSource = walkQuery.TrimStart("?"c)

            ElseIf walkUrl.IndexOf(
                       "#code=",
                       StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   walkUrl.IndexOf(
                       "#state=",
                       StringComparison.OrdinalIgnoreCase) >= 0 Then

                Dim hashIdx As Integer = walkUrl.IndexOf("#"c)

                If hashIdx >= 0 AndAlso hashIdx < walkUrl.Length - 1 Then
                    callbackParamSource = walkUrl.Substring(hashIdx + 1)
                End If
            End If

            Dim isOktaCallback As Boolean =
                Not String.IsNullOrWhiteSpace(callbackParamSource) AndAlso
                callbackParamSource.IndexOf(
                    "code=",
                    StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                callbackParamSource.IndexOf(
                    "state=",
                    StringComparison.OrdinalIgnoreCase) >= 0

            ' Capture authorize callback returnUrl from /login?returnUrl=...
            If Not String.IsNullOrWhiteSpace(walkQuery) AndAlso
               walkQuery.IndexOf(
                   "returnUrl=",
                   StringComparison.OrdinalIgnoreCase) >= 0 Then

                Dim returnMatch =
                    System.Text.RegularExpressions.Regex.Match(
                        walkQuery,
                        "(?:^|[?&])returnUrl=([^&]+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                If returnMatch.Success Then
                    Dim decodedReturn As String =
                        Uri.UnescapeDataString(returnMatch.Groups(1).Value)

                    If decodedReturn.StartsWith("/"c) Then
                        ssoReturnUrl =
                            "https://identity.axxessweb.com" &
                            decodedReturn
                    Else
                        ssoReturnUrl = decodedReturn
                    End If
                End If
            End If

            ' Handle Okta fragment/query callback BEFORE loop detection / GET.
            ' Fragments are never sent to the server, so challenge/okta must
            ' be called from the Location URL we already captured.
            If isOktaCallback AndAlso
               Not String.IsNullOrWhiteSpace(walkHost) AndAlso
               walkHost.IndexOf(
                   "identity.axxessweb.com",
                   StringComparison.OrdinalIgnoreCase) >= 0 Then

                Dim codeMatch =
                    System.Text.RegularExpressions.Regex.Match(
                        callbackParamSource,
                        "(?:^|[?&])code=([^&]+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                Dim stateMatch =
                    System.Text.RegularExpressions.Regex.Match(
                        callbackParamSource,
                        "(?:^|[?&])state=([^&]+)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                If Not codeMatch.Success OrElse Not stateMatch.Success Then
                    Throw New Exception(
                        "Okta callback URL was missing code/state: " &
                        walkUrl)
                End If

                Dim oktaCode As String =
                    Uri.UnescapeDataString(codeMatch.Groups(1).Value)

                Dim oktaState As String =
                    Uri.UnescapeDataString(stateMatch.Groups(1).Value)

                Dim challengeOktaUrl As String =
                    "https://identity.axxessweb.com/api/v1/challenge/okta" &
                    "?code=" & Uri.EscapeDataString(oktaCode) &
                    "&state=" & Uri.EscapeDataString(oktaState)

                Console.WriteLine(
                    "Posting Axxess Identity challenge/okta " &
                    "(code/state from callback fragment)...")

                Dim challengeOktaRequest =
                    CType(
                        System.Net.WebRequest.Create(challengeOktaUrl),
                        System.Net.HttpWebRequest)

                challengeOktaRequest.Method = "POST"
                challengeOktaRequest.CookieContainer = cookies
                challengeOktaRequest.AllowAutoRedirect = False
                challengeOktaRequest.ContentType = "application/json"
                challengeOktaRequest.Accept = "application/json"
                challengeOktaRequest.UserAgent = browserUserAgent
                challengeOktaRequest.Referer =
                    "https://identity.axxessweb.com/login"
                challengeOktaRequest.Headers.Add(
                    "Origin",
                    "https://identity.axxessweb.com")
                challengeOktaRequest.ContentLength = 0

                Dim challengeOktaBody As String = Nothing
                Dim challengeOktaStatus As Integer = 0

                Try
                    Using challengeOktaResponse =
                        CType(
                            challengeOktaRequest.GetResponse(),
                            System.Net.HttpWebResponse)

                        challengeOktaStatus =
                            CInt(challengeOktaResponse.StatusCode)

                        If challengeOktaResponse.GetResponseStream() IsNot Nothing Then
                            Using reader As New System.IO.StreamReader(
                                challengeOktaResponse.GetResponseStream())

                                challengeOktaBody = reader.ReadToEnd()
                            End Using
                        End If
                    End Using

                Catch challengeOktaEx As System.Net.WebException

                    If challengeOktaEx.Response Is Nothing Then
                        Throw
                    End If

                    Using challengeOktaResponse =
                        CType(
                            challengeOktaEx.Response,
                            System.Net.HttpWebResponse)

                        challengeOktaStatus =
                            CInt(challengeOktaResponse.StatusCode)

                        If challengeOktaResponse.GetResponseStream() IsNot Nothing Then
                            Using reader As New System.IO.StreamReader(
                                challengeOktaResponse.GetResponseStream())

                                challengeOktaBody = reader.ReadToEnd()
                            End Using
                        End If

                        Throw New Exception(
                            "Axxess challenge/okta failed. HTTP " &
                            challengeOktaStatus.ToString() &
                            ": " &
                            If(challengeOktaBody, ""))
                    End Using
                End Try

                ' SPA treats a null challenge/okta payload as validation failure.
                If String.IsNullOrWhiteSpace(challengeOktaBody) OrElse
                   challengeOktaBody.Trim().Equals(
                       "null",
                       StringComparison.OrdinalIgnoreCase) Then

                    Throw New Exception(
                        "Axxess challenge/okta returned null/empty. " &
                        "HTTP " &
                        challengeOktaStatus.ToString() &
                        ". Okta code/state were not accepted.")
                End If

                Console.WriteLine(
                    "Axxess Identity challenge/okta succeeded. " &
                    "Calling challenge/identity...")

                ' Browser finalize() always posts challenge/identity after SSO.
                Dim identityChallengeRequest =
                    CType(
                        System.Net.WebRequest.Create(
                            "https://identity.axxessweb.com/api/v1/challenge/identity"),
                        System.Net.HttpWebRequest)

                identityChallengeRequest.Method = "POST"
                identityChallengeRequest.CookieContainer = cookies
                identityChallengeRequest.AllowAutoRedirect = False
                identityChallengeRequest.ContentType = "application/json"
                identityChallengeRequest.Accept = "application/json"
                identityChallengeRequest.UserAgent = browserUserAgent
                identityChallengeRequest.Referer =
                    "https://identity.axxessweb.com/login"
                identityChallengeRequest.Headers.Add(
                    "Origin",
                    "https://identity.axxessweb.com")
                identityChallengeRequest.ContentLength = 0

                Dim identityChallengeBody As String = Nothing
                Dim identityChallengeStatus As Integer = 0

                Try
                    Using identityChallengeResponse =
                        CType(
                            identityChallengeRequest.GetResponse(),
                            System.Net.HttpWebResponse)

                        identityChallengeStatus =
                            CInt(identityChallengeResponse.StatusCode)

                        If identityChallengeResponse.GetResponseStream() IsNot Nothing Then
                            Using reader As New System.IO.StreamReader(
                                identityChallengeResponse.GetResponseStream())

                                identityChallengeBody =
                                    reader.ReadToEnd()
                            End Using
                        End If
                    End Using

                Catch identityChallengeEx As System.Net.WebException

                    If identityChallengeEx.Response Is Nothing Then
                        Throw
                    End If

                    Using identityChallengeResponse =
                        CType(
                            identityChallengeEx.Response,
                            System.Net.HttpWebResponse)

                        identityChallengeStatus =
                            CInt(identityChallengeResponse.StatusCode)

                        If identityChallengeResponse.GetResponseStream() IsNot Nothing Then
                            Using reader As New System.IO.StreamReader(
                                identityChallengeResponse.GetResponseStream())

                                identityChallengeBody =
                                    reader.ReadToEnd()
                            End Using
                        End If

                        Throw New Exception(
                            "Axxess challenge/identity failed. HTTP " &
                            identityChallengeStatus.ToString() &
                            ": " &
                            If(identityChallengeBody, ""))
                    End Using
                End Try

                Console.WriteLine(
                    "Axxess Identity challenge/identity succeeded. HTTP " &
                    identityChallengeStatus.ToString())

                visitedOkta = True
                identitySessionEstablished = True

                ' Same as SPA continueLogin(): open the original authorize callback.
                If Not String.IsNullOrWhiteSpace(ssoReturnUrl) Then

                    Console.WriteLine(
                        "Following Identity SSO returnUrl: " &
                        ssoReturnUrl)

                    Dim returnRequest =
                        CType(
                            System.Net.WebRequest.Create(ssoReturnUrl),
                            System.Net.HttpWebRequest)

                    returnRequest.Method = "GET"
                    returnRequest.CookieContainer = cookies
                    returnRequest.AllowAutoRedirect = False
                    returnRequest.Accept =
                        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
                    returnRequest.UserAgent = browserUserAgent
                    returnRequest.Referer =
                        "https://identity.axxessweb.com/login"

                    Dim returnLocation As String = Nothing
                    Dim returnBody As String = Nothing
                    Dim returnStatus As Integer = 0

                    Try
                        Using returnResponse =
                            CType(
                                returnRequest.GetResponse(),
                                System.Net.HttpWebResponse)

                            returnStatus = CInt(returnResponse.StatusCode)
                            returnLocation = returnResponse.Headers("Location")

                            If returnResponse.GetResponseStream() IsNot Nothing Then
                                Using reader As New System.IO.StreamReader(
                                    returnResponse.GetResponseStream())

                                    returnBody = reader.ReadToEnd()
                                End Using
                            End If
                        End Using

                    Catch returnEx As System.Net.WebException

                        If returnEx.Response Is Nothing Then
                            Throw
                        End If

                        Using returnResponse =
                            CType(
                                returnEx.Response,
                                System.Net.HttpWebResponse)

                            returnStatus = CInt(returnResponse.StatusCode)
                            returnLocation = returnResponse.Headers("Location")

                            If returnResponse.GetResponseStream() IsNot Nothing Then
                                Using reader As New System.IO.StreamReader(
                                    returnResponse.GetResponseStream())

                                    returnBody = reader.ReadToEnd()
                                End Using
                            End If
                        End Using
                    End Try

                    Console.WriteLine(
                        "SSO returnUrl status: " &
                        returnStatus.ToString())

                    If Not String.IsNullOrWhiteSpace(returnLocation) Then

                        If returnLocation.Contains("#") AndAlso
                           returnLocation.IndexOf(
                               "access_token=",
                               StringComparison.OrdinalIgnoreCase) >= 0 Then

                            identityAuthorizeLocation = returnLocation
                            tokenFragmentCaptured = True

                            Console.WriteLine(
                                "Access token fragment captured from SSO returnUrl.")

                            Exit Do
                        End If

                        ' Continue walking from the callback redirect.
                        walkReferer = ssoReturnUrl
                        walkUrl = returnLocation
                        Continue Do
                    End If
                End If

                Console.WriteLine(
                    "Identity session finalized. Retrying authorize...")

                Exit Do
            End If

            Dim normalizedWalkUrl As String =
                walkUrl.TrimEnd("/"c)

            Try
                Dim walkUriForKey As New Uri(walkUrl)
                Dim walkPathForKey As String =
                    walkUriForKey.AbsolutePath

                ' Query-only changes on login endpoints are still the same hop.
                If walkPathForKey.IndexOf(
                       "/login",
                       StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   walkPathForKey.IndexOf(
                       "/auth/login",
                       StringComparison.OrdinalIgnoreCase) >= 0 Then

                    normalizedWalkUrl =
                        walkUriForKey.Host &
                        walkPathForKey.TrimEnd("/"c)
                End If
            Catch
            End Try

            If Not visitedWalkUrls.Add(normalizedWalkUrl) Then

                Throw New Exception(
                    "Axxess Identity SSO looped on URL without completing " &
                    "challenge/okta: " &
                    walkUrl)
            End If

            Console.WriteLine(
                "Identity SSO hop " &
                walkHop.ToString() &
                ": " &
                walkUrl)

            If Not String.IsNullOrWhiteSpace(walkHost) AndAlso
               walkHost.IndexOf(
                   "okta.com",
                   StringComparison.OrdinalIgnoreCase) >= 0 Then

                visitedOkta = True
            End If

            ' Fragments must not be sent on the wire.
            Dim requestUrl As String = walkUrl
            Dim fragmentSeparator As Integer =
                requestUrl.IndexOf("#"c)

            If fragmentSeparator >= 0 Then
                requestUrl = requestUrl.Substring(0, fragmentSeparator)
            End If

            Dim walkRequest =
                CType(
                    System.Net.WebRequest.Create(requestUrl),
                    System.Net.HttpWebRequest)

            walkRequest.Method = "GET"
            walkRequest.CookieContainer = cookies
            walkRequest.AllowAutoRedirect = False
            walkRequest.Accept =
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
            walkRequest.UserAgent = browserUserAgent
            walkRequest.Referer = walkReferer
            walkRequest.Headers.Add(
                "Cache-Control",
                "no-cache")
            walkRequest.Headers.Add(
                "Pragma",
                "no-cache")

            Dim walkStatus As Integer = 0
            Dim walkLocation As String = Nothing
            Dim walkBody As String = Nothing

            Try
                Using walkResponse =
                    CType(
                        walkRequest.GetResponse(),
                        System.Net.HttpWebResponse)

                    walkStatus = CInt(walkResponse.StatusCode)
                    walkLocation = walkResponse.Headers("Location")

                    If walkResponse.GetResponseStream() IsNot Nothing Then
                        Using reader As New System.IO.StreamReader(
                            walkResponse.GetResponseStream())

                            walkBody = reader.ReadToEnd()
                        End Using
                    End If
                End Using

            Catch walkWebEx As System.Net.WebException

                If walkWebEx.Response Is Nothing Then
                    Throw
                End If

                Using walkResponse =
                    CType(
                        walkWebEx.Response,
                        System.Net.HttpWebResponse)

                    walkStatus = CInt(walkResponse.StatusCode)
                    walkLocation = walkResponse.Headers("Location")

                    If walkResponse.GetResponseStream() IsNot Nothing Then
                        Using reader As New System.IO.StreamReader(
                            walkResponse.GetResponseStream())

                            walkBody = reader.ReadToEnd()
                        End Using
                    End If
                End Using
            End Try

            Console.WriteLine(
                "Identity SSO hop " &
                walkHop.ToString() &
                " status: " &
                walkStatus.ToString())

            Dim nextWalkUrl As String = Nothing

            If Not String.IsNullOrWhiteSpace(walkLocation) Then

                If Uri.IsWellFormedUriString(
                    walkLocation,
                    UriKind.Absolute) Then

                    ' Prefer raw absolute Location to preserve fragments.
                    nextWalkUrl = walkLocation
                Else
                    Dim resolvedUri As New Uri(
                        New Uri(walkUrl),
                        walkLocation)

                    Dim fragmentFromLocation As String = Nothing
                    Dim hashIndex As Integer =
                        walkLocation.IndexOf("#"c)

                    If hashIndex >= 0 Then
                        fragmentFromLocation =
                            walkLocation.Substring(hashIndex)
                    End If

                    If Not String.IsNullOrEmpty(fragmentFromLocation) AndAlso
                       String.IsNullOrEmpty(resolvedUri.Fragment) Then

                        nextWalkUrl =
                            resolvedUri.GetLeftPart(UriPartial.Query) &
                            fragmentFromLocation
                    ElseIf Not String.IsNullOrEmpty(resolvedUri.Fragment) Then
                        nextWalkUrl = resolvedUri.OriginalString
                    Else
                        nextWalkUrl = resolvedUri.AbsoluteUri
                    End If
                End If

            ElseIf Not String.IsNullOrWhiteSpace(walkBody) Then

                ' Prefer Okta authorize URLs. Ignore bare identity roots that
                ' caused the previous hop loop.
                Dim q As String = Chr(34).ToString()
                Dim candidatePatterns As String() = {
                    "https://helpathome\.okta\.com/oauth2/[^\s'<>\\]+",
                    "https://[^\s'<>\\]*okta\.com/oauth2/[^\s'<>\\]+",
                    "https://[^\s'<>\\]*okta\.com/app/[^\s'<>\\]+",
                    "(?:href|action|window\.location(?:\.href)?)\s*=\s*['" & q & "](https?://[^'" & q & "]*okta\.com[^'" & q & "]*)['" & q & "]",
                    "(?:href|action|window\.location(?:\.href)?)\s*=\s*['" & q & "](/[^'" & q & "]*(?:challenge|external|login|authorize)[^'" & q & "]*)['" & q & "]",
                    "https://identity\.axxessweb\.com/(?:api|signin|challenge|connect)/[^\s'<>\\]+"
                }

                For Each pattern As String In candidatePatterns

                    Dim htmlUrlMatch =
                        System.Text.RegularExpressions.Regex.Match(
                            walkBody,
                            pattern,
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                    If Not htmlUrlMatch.Success Then
                        Continue For
                    End If

                    Dim candidate As String

                    If htmlUrlMatch.Groups.Count > 1 AndAlso
                       Not String.IsNullOrWhiteSpace(
                           htmlUrlMatch.Groups(1).Value) Then

                        candidate = htmlUrlMatch.Groups(1).Value
                    Else
                        candidate = htmlUrlMatch.Value
                    End If

                    candidate =
                        System.Net.WebUtility.HtmlDecode(candidate).Trim()

                    If candidate.StartsWith("/"c) Then
                        candidate =
                            New Uri(
                                New Uri("https://identity.axxessweb.com"),
                                candidate).AbsoluteUri
                    End If

                    If Not Uri.IsWellFormedUriString(
                        candidate,
                        UriKind.Absolute) Then

                        Continue For
                    End If

                    Dim candidateUri As Uri = Nothing

                    Try
                        candidateUri = New Uri(candidate)
                    Catch
                        Continue For
                    End Try

                    Dim candidatePath As String =
                        candidateUri.AbsolutePath.TrimEnd("/"c)

                    ' Reject bare site roots that create infinite hops.
                    If String.IsNullOrEmpty(candidatePath) Then
                        Continue For
                    End If

                    nextWalkUrl = candidate
                    Exit For
                Next
            End If

            ' Identity /login is an Aurelia SPA. Okta URL comes from
            ' POST /api/v1/challenge/domain or /api/v1/challenge/email.
            If String.IsNullOrWhiteSpace(nextWalkUrl) AndAlso
               Not String.IsNullOrWhiteSpace(walkHost) AndAlso
               walkHost.IndexOf(
                   "identity.axxessweb.com",
                   StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
               walkUrl.IndexOf(
                   "/login",
                   StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
               Not String.IsNullOrWhiteSpace(walkBody) AndAlso
               (walkBody.IndexOf(
                    "aurelia-app",
                    StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                walkBody.IndexOf(
                    "bundle.main",
                    StringComparison.OrdinalIgnoreCase) >= 0) Then

                Console.WriteLine(
                    "Identity SPA login detected. Requesting Okta redirectUrl...")

                Dim challengeJsonText As String = Nothing
                Dim challengeRedirectUrl As String = Nothing

                ' 1) Domain challenge for helpathome.com
                Dim domainPayload As String =
                    Newtonsoft.Json.JsonConvert.SerializeObject(
                        New With {
                            .domain = "helpathome.com"
                        })

                Dim domainBytes =
                    System.Text.Encoding.UTF8.GetBytes(domainPayload)

                Dim domainRequest =
                    CType(
                        System.Net.WebRequest.Create(
                            "https://identity.axxessweb.com/api/v1/challenge/domain"),
                        System.Net.HttpWebRequest)

                domainRequest.Method = "POST"
                domainRequest.CookieContainer = cookies
                domainRequest.AllowAutoRedirect = False
                domainRequest.ContentType = "application/json"
                domainRequest.Accept = "application/json"
                domainRequest.UserAgent = browserUserAgent
                domainRequest.Referer = walkUrl
                domainRequest.Headers.Add(
                    "Origin",
                    "https://identity.axxessweb.com")
                domainRequest.ContentLength = domainBytes.Length

                Using domainStream = domainRequest.GetRequestStream()
                    domainStream.Write(domainBytes, 0, domainBytes.Length)
                End Using

                Try
                    Using domainResponse =
                        CType(
                            domainRequest.GetResponse(),
                            System.Net.HttpWebResponse)

                        Using reader As New System.IO.StreamReader(
                            domainResponse.GetResponseStream())

                            challengeJsonText = reader.ReadToEnd()
                        End Using
                    End Using

                Catch domainEx As System.Net.WebException

                    If domainEx.Response IsNot Nothing Then
                        Using domainResponse =
                            CType(
                                domainEx.Response,
                                System.Net.HttpWebResponse)

                            Using reader As New System.IO.StreamReader(
                                domainResponse.GetResponseStream())

                                challengeJsonText = reader.ReadToEnd()
                            End Using
                        End Using
                    End If
                End Try

                If Not String.IsNullOrWhiteSpace(challengeJsonText) AndAlso
                   Not challengeJsonText.TrimStart().StartsWith("<") Then

                    Dim domainJson =
                        Newtonsoft.Json.Linq.JObject.Parse(
                            challengeJsonText)

                    If domainJson("redirectUrl") IsNot Nothing Then
                        challengeRedirectUrl =
                            domainJson("redirectUrl").ToString()
                    End If
                End If

                ' 2) Email challenge fallback using username argument.
                If String.IsNullOrWhiteSpace(challengeRedirectUrl) Then

                    If String.IsNullOrWhiteSpace(identityEmail) Then
                        Throw New Exception(
                            "Identity SPA login requires username " &
                            "(email or user@helpathome.com) to call " &
                            "challenge/email after challenge/domain " &
                            "did not return redirectUrl. Domain response: " &
                            If(challengeJsonText, ""))
                    End If

                    Console.WriteLine(
                        "Domain challenge had no redirectUrl. " &
                        "Trying challenge/email...")

                    Dim emailPayload As String =
                        Newtonsoft.Json.JsonConvert.SerializeObject(
                            New With {
                                .email = identityEmail
                            })

                    Dim emailBytes =
                        System.Text.Encoding.UTF8.GetBytes(emailPayload)

                    Dim emailRequest =
                        CType(
                            System.Net.WebRequest.Create(
                                "https://identity.axxessweb.com/api/v1/challenge/email"),
                            System.Net.HttpWebRequest)

                    emailRequest.Method = "POST"
                    emailRequest.CookieContainer = cookies
                    emailRequest.AllowAutoRedirect = False
                    emailRequest.ContentType = "application/json"
                    emailRequest.Accept = "application/json"
                    emailRequest.UserAgent = browserUserAgent
                    emailRequest.Referer = walkUrl
                    emailRequest.Headers.Add(
                        "Origin",
                        "https://identity.axxessweb.com")
                    emailRequest.ContentLength = emailBytes.Length

                    Using emailStream = emailRequest.GetRequestStream()
                        emailStream.Write(emailBytes, 0, emailBytes.Length)
                    End Using

                    challengeJsonText = Nothing

                    Try
                        Using emailResponse =
                            CType(
                                emailRequest.GetResponse(),
                                System.Net.HttpWebResponse)

                            Using reader As New System.IO.StreamReader(
                                emailResponse.GetResponseStream())

                                challengeJsonText = reader.ReadToEnd()
                            End Using
                        End Using

                    Catch emailEx As System.Net.WebException

                        If emailEx.Response Is Nothing Then
                            Throw
                        End If

                        Using emailResponse =
                            CType(
                                emailEx.Response,
                                System.Net.HttpWebResponse)

                            Using reader As New System.IO.StreamReader(
                                emailResponse.GetResponseStream())

                                challengeJsonText = reader.ReadToEnd()
                            End Using

                            Throw New Exception(
                                "Axxess challenge/email failed. HTTP " &
                                CInt(emailResponse.StatusCode).ToString() &
                                ": " &
                                If(challengeJsonText, ""))
                        End Using
                    End Try

                    If String.IsNullOrWhiteSpace(challengeJsonText) OrElse
                       challengeJsonText.TrimStart().StartsWith("<") Then

                        Throw New Exception(
                            "Axxess challenge/email returned a non-JSON response.")
                    End If

                    Dim emailJson =
                        Newtonsoft.Json.Linq.JObject.Parse(
                            challengeJsonText)

                    If emailJson("redirectUrl") IsNot Nothing Then
                        challengeRedirectUrl =
                            emailJson("redirectUrl").ToString()
                    End If

                    If String.IsNullOrWhiteSpace(challengeRedirectUrl) Then
                        Throw New Exception(
                            "Axxess challenge/email did not return redirectUrl. Response: " &
                            challengeJsonText)
                    End If
                End If

                nextWalkUrl = challengeRedirectUrl

                Console.WriteLine(
                    "Identity challenge returned Okta redirectUrl.")
            End If

            ' After Okta, a settled Identity/Central page means session cookies
            ' should exist even without a further Location header.
            If String.IsNullOrWhiteSpace(nextWalkUrl) Then

                If visitedOkta AndAlso
                   Not String.IsNullOrWhiteSpace(walkHost) AndAlso
                   (walkHost.IndexOf(
                        "identity.axxessweb.com",
                        StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                    walkHost.IndexOf(
                        "central.axxessweb.com",
                        StringComparison.OrdinalIgnoreCase) >= 0) Then

                    identitySessionEstablished = True

                    Console.WriteLine(
                        "Axxess Identity SSO settled on " &
                        walkUrl &
                        ". Retrying authorize...")

                    Exit Do
                End If

                Dim bodyPreview As String =
                    If(
                        String.IsNullOrWhiteSpace(walkBody),
                        "",
                        walkBody.Substring(
                            0,
                            Math.Min(1000, walkBody.Length)))

                Throw New Exception(
                    "Axxess Identity SSO chain stopped with HTTP " &
                    walkStatus.ToString() &
                    " at " &
                    walkUrl &
                    ". No Okta authorize URL was found. Response starts with: " &
                    bodyPreview)
            End If

            ' Token fragment returned during the login chain.
            If nextWalkUrl.Contains("#") AndAlso
               nextWalkUrl.IndexOf(
                   "access_token=",
                   StringComparison.OrdinalIgnoreCase) >= 0 Then

                identityAuthorizeLocation = nextWalkUrl
                tokenFragmentCaptured = True
                identitySessionEstablished = True

                Console.WriteLine(
                    "Axxess access token fragment captured during Identity SSO.")

                Exit Do
            End If

            Dim nextHost As String = Nothing
            Dim nextPath As String = Nothing

            Try
                Dim nextUri As New Uri(nextWalkUrl)
                nextHost = nextUri.Host
                nextPath = nextUri.AbsolutePath.TrimEnd("/"c)
            Catch
                nextHost = Nothing
                nextPath = Nothing
            End Try

            ' Ignore useless redirects to the bare Identity origin.
            If Not String.IsNullOrWhiteSpace(nextHost) AndAlso
               nextHost.IndexOf(
                   "identity.axxessweb.com",
                   StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
               String.IsNullOrEmpty(nextPath) Then

                If visitedOkta Then
                    identitySessionEstablished = True

                    Console.WriteLine(
                        "Ignoring bare Identity root after Okta SSO. " &
                        "Retrying authorize...")

                    Exit Do
                End If

                Throw New Exception(
                    "Axxess Identity login redirected to the bare Identity root " &
                    "without an Okta authorize URL. Last page: " &
                    walkUrl &
                    ". Response starts with: " &
                    If(
                        String.IsNullOrWhiteSpace(walkBody),
                        "",
                        walkBody.Substring(
                            0,
                            Math.Min(1000, walkBody.Length))))
            End If

            If Not String.IsNullOrWhiteSpace(nextHost) AndAlso
               nextHost.IndexOf(
                   "okta.com",
                   StringComparison.OrdinalIgnoreCase) >= 0 Then

                visitedOkta = True
            End If

            Console.WriteLine(
                "Identity SSO redirecting to: " &
                nextWalkUrl)

            ' After Okta SSO returns to Identity/Central (and we already
            ' requested that return URL), retry authorize.
            If visitedOkta AndAlso
               Not String.IsNullOrWhiteSpace(walkHost) AndAlso
               (walkHost.IndexOf(
                    "identity.axxessweb.com",
                    StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                walkHost.IndexOf(
                    "central.axxessweb.com",
                    StringComparison.OrdinalIgnoreCase) >= 0) AndAlso
               walkUrl.IndexOf(
                   "/api/v1/auth/login",
                   StringComparison.OrdinalIgnoreCase) < 0 AndAlso
               walkUrl.IndexOf(
                   "/login",
                   StringComparison.OrdinalIgnoreCase) < 0 Then

                identitySessionEstablished = True

                Console.WriteLine(
                    "Axxess Identity session established after Okta SSO. " &
                    "Retrying authorize...")

                Exit Do
            End If

            walkReferer = walkUrl
            walkUrl = nextWalkUrl
        Loop

        If tokenFragmentCaptured Then
            Exit Do
        End If

        If Not identitySessionEstablished Then

            If walkHop >= maximumWalkHops Then
                Throw New Exception(
                    "Axxess Identity SSO exceeded the maximum number of redirects. " &
                    "Last URL: " &
                    walkUrl &
                    ". Visited Okta: " &
                    visitedOkta.ToString() &
                    ".")
            End If

            Throw New Exception(
                "Axxess Identity SSO did not complete. Last URL: " &
                walkUrl &
                ". Visited Okta: " &
                visitedOkta.ToString() &
                ".")
        End If

        ' Loop continues to retry /connect/authorize.
    Loop

    If Not tokenFragmentCaptured AndAlso
       (String.IsNullOrWhiteSpace(identityAuthorizeLocation) OrElse
        Not identityAuthorizeLocation.Contains("#") OrElse
        identityAuthorizeLocation.IndexOf(
            "access_token=",
            StringComparison.OrdinalIgnoreCase) < 0) Then

        Throw New Exception(
            "Axxess authorize did not return an access_token fragment. Location: " &
            If(identityAuthorizeLocation, "EMPTY"))
    End If

    Dim fragmentIndex As Integer =
        identityAuthorizeLocation.IndexOf("#"c)

    If fragmentIndex < 0 OrElse
       fragmentIndex >= identityAuthorizeLocation.Length - 1 Then

        Throw New Exception(
            "Authorize Location contained an empty token fragment.")
    End If

    Dim tokenFragment As String =
        identityAuthorizeLocation.Substring(
            fragmentIndex + 1)

    accessToken = Nothing

    For Each fragmentItem As String _
        In tokenFragment.Split("&"c)

        Dim equalsIndex As Integer =
            fragmentItem.IndexOf("="c)

        If equalsIndex <= 0 Then
            Continue For
        End If

        Dim key As String =
            fragmentItem.Substring(
                0,
                equalsIndex)

        Dim value As String =
            Uri.UnescapeDataString(
                fragmentItem.Substring(
                    equalsIndex + 1))

        If key.Equals(
            "access_token",
            StringComparison.OrdinalIgnoreCase) Then

            accessToken = value
            Exit For
        End If
    Next

    If String.IsNullOrWhiteSpace(accessToken) Then
        Throw New Exception(
            "access_token was not found in the Axxess authorize Location.")
    End If

    Console.WriteLine(
        "Axxess access token extracted successfully.")

    ' Do not print or log the access token.

    ' =====================================================
    ' 2. GET ORGANIZATIONS AND RESOLVE HOMECARE IDs
    ' =====================================================

    Console.WriteLine(
        "Resolving HomeCare agencyId and userId...")

    Dim organizationsUrl As String =
        "https://central.axxessweb.com/api/v1/meta/organizations"

    Dim organizationsRequest =
        CType(
            System.Net.WebRequest.Create(organizationsUrl),
            System.Net.HttpWebRequest)

    organizationsRequest.Method = "GET"
    organizationsRequest.CookieContainer = cookies
    organizationsRequest.Accept = "*/*"
    organizationsRequest.ContentType =
        "application/json"
    organizationsRequest.UserAgent =
        browserUserAgent
    organizationsRequest.Referer =
        "https://central.axxessweb.com/help"

    organizationsRequest.Headers.Add(
        "Authorization",
        "Bearer " & accessToken)

    organizationsRequest.Headers.Add(
        "x-time-zone",
        "America/Chicago")

    organizationsRequest.Headers.Add(
        "Cache-Control",
        "no-cache")

    organizationsRequest.Headers.Add(
        "Pragma",
        "no-cache")

    Dim organizationsResponseText As String = Nothing

    Using response =
        CType(
            organizationsRequest.GetResponse(),
            System.Net.HttpWebResponse)

        Using reader As New System.IO.StreamReader(
            response.GetResponseStream())

            organizationsResponseText =
                reader.ReadToEnd()
        End Using
    End Using

    If String.IsNullOrWhiteSpace(
        organizationsResponseText) Then

        Throw New Exception(
            "Organizations response was empty.")
    End If

    If organizationsResponseText.TrimStart().StartsWith("<") Then
        Throw New Exception(
            "Expected JSON from organizations endpoint but received HTML. " &
            "Response starts with: " &
            organizationsResponseText.Substring(
                0,
                Math.Min(
                    500,
                    organizationsResponseText.Length)))
    End If

    Dim organizationsArray =
        Newtonsoft.Json.Linq.JArray.Parse(
            organizationsResponseText)

    For Each organization As Newtonsoft.Json.Linq.JObject _
        In organizationsArray

        Dim organizationId As String =
            If(
                organization("id"),
                "").ToString()

        Dim agencies =
            TryCast(
                organization("agencies"),
                Newtonsoft.Json.Linq.JArray)

        If agencies Is Nothing Then
            Continue For
        End If

        For Each agency As Newtonsoft.Json.Linq.JObject _
            In agencies

            Dim applicationValue As Integer

            If agency("application") Is Nothing OrElse
               Not Integer.TryParse(
                   agency("application").ToString(),
                   applicationValue) Then

                Continue For
            End If

            ' Application 2 represents Axxess HomeCare.
            If applicationValue = 2 Then

                agencyId =
                    organizationId

                If agency("userId") IsNot Nothing Then
                    userId =
                        agency("userId").ToString()
                End If

                Exit For
            End If
        Next

        If Not String.IsNullOrWhiteSpace(agencyId) AndAlso
           Not String.IsNullOrWhiteSpace(userId) Then

            Exit For
        End If
    Next

    If String.IsNullOrWhiteSpace(agencyId) Then
        Throw New Exception(
            "Could not find an organization containing application 2 for HomeCare.")
    End If

    If String.IsNullOrWhiteSpace(userId) Then
        Throw New Exception(
            "Could not find the HomeCare userId for application 2.")
    End If

    Console.WriteLine(
        "HomeCare agencyId and userId resolved successfully.")

    ' =====================================================
    ' 3. NAVIGATE FROM CENTRAL TO HOMECARE
    ' =====================================================

    Console.WriteLine(
        "Requesting HomeCare navigation URL...")

    Dim navigateUrl As String =
        "https://central.axxessweb.com/api/v1/navigate/agency" &
        "?agencyId=" &
            Uri.EscapeDataString(agencyId) &
        "&applicationId=2" &
        "&isClinician=true" &
        "&userId=" &
            Uri.EscapeDataString(userId)

    Dim navigateRequest =
        CType(
            System.Net.WebRequest.Create(navigateUrl),
            System.Net.HttpWebRequest)

    navigateRequest.Method = "GET"
    navigateRequest.CookieContainer = cookies
    navigateRequest.Accept =
        "application/json, text/plain, */*"
    navigateRequest.UserAgent =
        browserUserAgent
    navigateRequest.Referer =
        "https://central.axxessweb.com/help"

    navigateRequest.Headers.Add(
        "Authorization",
        "Bearer " & accessToken)

    navigateRequest.Headers.Add(
        "x-time-zone",
        "America/Chicago")

    navigateRequest.Headers.Add(
        "Cache-Control",
        "no-cache")

    navigateRequest.Headers.Add(
        "Pragma",
        "no-cache")

    Dim navigateResponseText As String = Nothing

    Using response =
        CType(
            navigateRequest.GetResponse(),
            System.Net.HttpWebResponse)

        Using reader As New System.IO.StreamReader(
            response.GetResponseStream())

            navigateResponseText =
                reader.ReadToEnd()
        End Using
    End Using

    If String.IsNullOrWhiteSpace(
        navigateResponseText) Then

        Throw New Exception(
            "The Central navigate/agency response was empty.")
    End If

    If navigateResponseText.TrimStart().StartsWith("<") Then
        Throw New Exception(
            "Expected JSON from navigate/agency but received HTML. " &
            "Response starts with: " &
            navigateResponseText.Substring(
                0,
                Math.Min(
                    500,
                    navigateResponseText.Length)))
    End If

    Dim navigateJson =
        Newtonsoft.Json.Linq.JObject.Parse(
            navigateResponseText)

    Dim homeCareRedirectUrl As String = Nothing

    If navigateJson("redirectUrl") IsNot Nothing Then
        homeCareRedirectUrl =
            navigateJson("redirectUrl").ToString()
    End If

    If String.IsNullOrWhiteSpace(
        homeCareRedirectUrl) Then

        Dim navigateError As String = Nothing

        If navigateJson("errorMessage") IsNot Nothing Then
            navigateError =
                navigateJson("errorMessage").ToString()
        End If

        Throw New Exception(
            "HomeCare redirectUrl was not returned." &
            If(
                String.IsNullOrWhiteSpace(navigateError),
                "",
                " Axxess message: " & navigateError))
    End If

    Console.WriteLine(
        "HomeCare navigation URL received.")

    ' =====================================================
    ' 4. OPEN HOMECARE AND ESTABLISH SESSION
    ' =====================================================

    Console.WriteLine(
        "Establishing HomeCare session...")

    Dim homeCareRequest =
        CType(
            System.Net.WebRequest.Create(
                homeCareRedirectUrl),
            System.Net.HttpWebRequest)

    homeCareRequest.Method = "GET"
    homeCareRequest.CookieContainer = cookies
    homeCareRequest.AllowAutoRedirect = True
    homeCareRequest.Accept =
        "text/html,application/xhtml+xml,application/xml;q=0.9," &
        "image/avif,image/webp,image/apng,*/*;q=0.8"
    homeCareRequest.UserAgent =
        browserUserAgent
    homeCareRequest.Referer =
        "https://central.axxessweb.com/help"

    homeCareRequest.Headers.Add(
        "Cache-Control",
        "no-cache")

    homeCareRequest.Headers.Add(
        "Pragma",
        "no-cache")

    Dim homeCareHtml As String = Nothing
    Dim finalHomeCareUrl As String = Nothing

    Using response =
        CType(
            homeCareRequest.GetResponse(),
            System.Net.HttpWebResponse)

        finalHomeCareUrl =
            response.ResponseUri.AbsoluteUri

        Using reader As New System.IO.StreamReader(
            response.GetResponseStream())

            homeCareHtml =
                reader.ReadToEnd()
        End Using
    End Using

    axxessResponse =
        homeCareHtml

    If String.IsNullOrWhiteSpace(
        homeCareHtml) Then

        Throw New Exception(
            "HomeCare returned an empty response.")
    End If

    If String.IsNullOrWhiteSpace(
        finalHomeCareUrl) Then

        Throw New Exception(
            "HomeCare final URL was empty.")
    End If

    If Not finalHomeCareUrl.StartsWith(
        "https://homecare1.axxessweb.com",
        StringComparison.OrdinalIgnoreCase) Then

        Throw New Exception(
            "HomeCare navigation finished at an unexpected URL: " &
            finalHomeCareUrl &
            ". Response starts with: " &
            homeCareHtml.Substring(
                0,
                Math.Min(
                    500,
                    homeCareHtml.Length)))
    End If

    Dim hasHomeCareMarker As Boolean =
        homeCareHtml.IndexOf(
            "Axxess HomeCare",
            StringComparison.OrdinalIgnoreCase) >= 0 OrElse
        homeCareHtml.IndexOf(
            "BuildDateIdentifier",
            StringComparison.OrdinalIgnoreCase) >= 0 OrElse
        homeCareHtml.IndexOf(
            "homecare",
            StringComparison.OrdinalIgnoreCase) >= 0

    If Not hasHomeCareMarker Then
        Throw New Exception(
            "HomeCare session could not be confirmed. " &
            "Final URL: " &
            finalHomeCareUrl &
            ". Response starts with: " &
            homeCareHtml.Substring(
                0,
                Math.Min(
                    500,
                    homeCareHtml.Length)))
    End If

    Console.WriteLine(
        "HomeCare session established successfully.")

    ' =====================================================
    ' 5. EXTRACT BUILD DATE IDENTIFIER
    ' =====================================================

    Dim buildMatch =
        System.Text.RegularExpressions.Regex.Match(
            homeCareHtml,
            "BuildDateIdentifier\s*[:=]\s*['""]([^'""]+)['""]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)

    If Not buildMatch.Success Then

        buildMatch =
            System.Text.RegularExpressions.Regex.Match(
                homeCareHtml,
                "[""']BuildDateIdentifier[""']\s*:\s*[""']([^""']+)[""']",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
    End If

    If Not buildMatch.Success Then
        Throw New Exception(
            "HomeCare session was established, but " &
            "BuildDateIdentifier could not be located. " &
            "Response starts with: " &
            homeCareHtml.Substring(
                0,
                Math.Min(
                    1000,
                    homeCareHtml.Length)))
    End If

    buildDateIdentifier =
        buildMatch.Groups(1).Value.Trim()

    If String.IsNullOrWhiteSpace(
        buildDateIdentifier) Then

        Throw New Exception(
            "The extracted BuildDateIdentifier was empty.")
    End If

    Console.WriteLine(
        "BuildDateIdentifier extracted successfully.")

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
        errorMessage =
            ex.Message
    End If

Catch ex As Exception

    errorMessage =
        ex.Message

End Try
