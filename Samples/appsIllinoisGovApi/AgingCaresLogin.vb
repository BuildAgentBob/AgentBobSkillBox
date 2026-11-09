' ============================================================
' ILLINOIS AGINGCARES LOGIN (ADFS / SP2 / WS-Fed)
' UiPath Invoke Code - VB.NET
'
' IN:  appUsername, appPassword (String)
' IN/OUT: cookies, errorMessage
' OUT: sessionData (Dictionary — Trace, FinalUrl, PendingRedirect, etc.)
' Do not add other Invoke Code arguments; extras live in sessionData or locals.
'
' Capture chain (BobScout):
'   1) POST UserName/Password on fspartner.illinois.gov  -> MSISAuth
'   2) GET  fspartner -> auto-form (wa/wresult/wctx)
'   3) POST fsresource.illinois.gov/adfs/ls/             -> resource token
'   4) POST webapps.../cms/SP2/                          -> SPAuth*
'   5) GET  /CMS/SP2/?wa=...&wtrealm=AgingCares...
'   6) POST token form into AgingCares                   -> ProvidersIndex 200
'
' FIXES:
' - Do not loop forever on fsresource HomeRealm email posts.
' - If stuck on fsresource without a password form, jump to fspartner
'   (partner IdP) with wtrealm=fsresource trust — that is where
'   DOMAIN\user + FormsAuthentication is posted in the capture.
' - SPAuth alone is never success; require AgingCares HTTP 200.
' - Strip :443 from URLs for CookieContainer.
' ============================================================
' Locals used in Catch — UiPath Invoke Code does not see Try-scoped Dim in Catch.

Dim cookieJar As System.Net.CookieContainer = Nothing
Dim appPasswordPlain As String = Nothing
Dim appUser As String = ""
Dim loginTrace As New System.Collections.Generic.List(Of String)()
Dim currentUrl As String = ""
Dim currentStatus As Integer = 0
Dim currentLocation As String = ""
Dim tokenPosts As Integer = 0
Dim loginPosted As Boolean = False
Dim partnerJumpDone As Boolean = False

Dim CookieNamesFromJar As Func(Of System.Net.CookieContainer, String) =
    Function(jar As System.Net.CookieContainer) As String
        Dim names As New System.Collections.Generic.List(Of String)()
        If jar Is Nothing Then Return ""
        For Each cookieUri As String In New String() {
            "https://webapps.illinois.gov/",
            "https://webapps.illinois.gov/CMS/SP2/",
            "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/",
            "https://fspartner.illinois.gov/",
            "https://fsresource.illinois.gov/"
        }
            Try
                For Each c As System.Net.Cookie In jar.GetCookies(New System.Uri(cookieUri))
                    If Not names.Contains(c.Name) Then
                        names.Add(c.Name)
                    End If
                Next
            Catch
            End Try
        Next
        Return String.Join(",", names)
    End Function

Try
    errorMessage = ""
    loginTrace.Clear()

    appPasswordPlain = If(appPassword, "").Trim()
    appUser = If(appUsername, "").Trim()

    sessionData = New System.Collections.Generic.Dictionary(Of String, Object)(
        StringComparer.OrdinalIgnoreCase
    )

    sessionData("FinalUrl") = ""
    sessionData("IsLoggedIn") = False
    sessionData("Trace") = ""
    sessionData("StatusCode") = 0
    sessionData("CookieNames") = ""
    sessionData("PendingRedirect") = ""
    sessionData("baseUrl") = "https://webapps.illinois.gov"

    If cookies Is Nothing Then
        cookies = New System.Net.CookieContainer()
    End If

    cookieJar = cookies

    If String.IsNullOrWhiteSpace(appUser) Then
        Throw New System.Exception("appUsername is required.")
    End If

    If String.IsNullOrWhiteSpace(appPasswordPlain) Then
        Throw New System.Exception("appPassword is required.")
    End If

    Dim CollectCookieNames As Func(Of String) =
        Function() As String
            Return CookieNamesFromJar(cookieJar)
        End Function

    Dim LogCookies As Action(Of String) =
        Sub(label As String)
            Dim snap As String = CollectCookieNames()
            loginTrace.Add("COOKIES | " & label & " | " & snap)
            Console.WriteLine("COOKIES | " & label & " | " & snap)
        End Sub

    Dim startUrl As String =
        "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/CMIS/CMIS/ProvidersIndex"

    Dim fsResourceTrust As String =
        "http://fsresource.illinois.gov/adfs/services/trust"

    Dim partnerTrust As String =
        "http://fspartner.illinois.gov/adfs/services/trust"

    Dim partnerAdfsBase As String =
        "https://fspartner.illinois.gov/adfs/ls/"

    Dim resourceAdfsHost As String =
        "fsresource.illinois.gov"

    Dim partnerAdfsHost As String =
        "fspartner.illinois.gov"

    Console.WriteLine("Starting marked action: AgingCares Login")

    ' Prefer TLS 1.2 only — offering TLS 1.0/1.1 can make some hosts RST the socket.
    System.Net.ServicePointManager.SecurityProtocol =
        System.Net.SecurityProtocolType.Tls12

    Try
        ' TLS 1.3 when the runtime supports it (enum value 12288).
        System.Net.ServicePointManager.SecurityProtocol =
            System.Net.SecurityProtocolType.Tls12 Or
            CType(12288, System.Net.SecurityProtocolType)
    Catch
    End Try

    System.Net.ServicePointManager.Expect100Continue = False
    System.Net.ServicePointManager.DefaultConnectionLimit = 20
    System.Net.ServicePointManager.CheckCertificateRevocationList = False

    Dim NormalizeUrl As Func(Of String, String) =
        Function(url As String) As String
            If String.IsNullOrWhiteSpace(url) Then
                Return ""
            End If
            Return url.Replace(":443/", "/").Replace(":443?", "?")
        End Function


    Dim IsAgingCaresAppUrl As Func(Of String, Boolean) =
        Function(url As String) As Boolean
            If String.IsNullOrWhiteSpace(url) Then
                Return False
            End If
            If url.IndexOf("/AGE/AgingCares.CaseManagement/", StringComparison.OrdinalIgnoreCase) < 0 Then
                Return False
            End If
            If url.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return False
            End If
            If url.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return False
            End If
            Return True
        End Function


    Dim IsAdfsUrl As Func(Of String, Boolean) =
        Function(url As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(url) AndAlso
                   url.IndexOf("/adfs/", StringComparison.OrdinalIgnoreCase) >= 0
        End Function


    Dim HostIs As Func(Of String, String, Boolean) =
        Function(url As String, host As String) As Boolean
            Try
                Return New System.Uri(url).Host.Equals(
                    host,
                    StringComparison.OrdinalIgnoreCase
                )
            Catch
                Return False
            End Try
        End Function


    Dim GetQueryValue As Func(Of String, String, String) =
        Function(url As String, key As String) As String
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


    Dim BuildWhrSelectUrl As Func(Of String, String) =
        Function(fromUrl As String) As String
            ' Browser-like ADFS home-realm selection: stay on fsresource,
            ' add whr=partner trust, follow the 302 to fspartner.
            If String.IsNullOrWhiteSpace(fromUrl) Then
                Return ""
            End If

            Dim existingWhr As String = GetQueryValue(fromUrl, "whr")
            If Not String.IsNullOrWhiteSpace(existingWhr) Then
                Return NormalizeUrl(fromUrl)
            End If

            Dim joiner As String = If(fromUrl.Contains("?"), "&", "?")
            Return NormalizeUrl(
                fromUrl &
                joiner &
                "whr=" &
                System.Uri.EscapeDataString(partnerTrust)
            )
        End Function


    Dim BuildPartnerLoginUrl As Func(Of String, String) =
        Function(fromUrl As String) As String
            ' Fallback: hit fspartner directly (can be RST by edge/WAF).
            ' Capture posts credentials there with:
            '   wtrealm = http://fsresource.illinois.gov/adfs/services/trust
            Dim wa As String = GetQueryValue(fromUrl, "wa")
            Dim wctx As String = GetQueryValue(fromUrl, "wctx")
            Dim wct As String = GetQueryValue(fromUrl, "wct")
            Dim clientId As String = GetQueryValue(fromUrl, "client-request-id")

            If String.IsNullOrWhiteSpace(wa) Then
                wa = "wsignin1.0"
            End If

            Dim sb As New System.Text.StringBuilder()
            sb.Append(partnerAdfsBase)
            sb.Append("?wa=").Append(System.Uri.EscapeDataString(wa))
            sb.Append("&wtrealm=").Append(System.Uri.EscapeDataString(fsResourceTrust))

            If Not String.IsNullOrWhiteSpace(wctx) Then
                sb.Append("&wctx=").Append(System.Uri.EscapeDataString(wctx))
            End If
            If Not String.IsNullOrWhiteSpace(wct) Then
                sb.Append("&wct=").Append(System.Uri.EscapeDataString(wct))
            End If
            If Not String.IsNullOrWhiteSpace(clientId) Then
                sb.Append("&client-request-id=").Append(System.Uri.EscapeDataString(clientId))
            End If

            Return NormalizeUrl(sb.ToString())
        End Function


    Dim ToRealmEmail As Func(Of String, String) =
        Function(user As String) As String
            If String.IsNullOrWhiteSpace(user) Then
                Return ""
            End If
            If user.Contains("@") Then
                Return user
            End If
            If user.Contains("\") Then
                Return user.Substring(user.LastIndexOf("\"c) + 1) &
                       "@external.illinois.gov"
            End If
            Return user & "@external.illinois.gov"
        End Function


    Dim BuildFormBody As Func(
        Of System.Collections.Generic.Dictionary(Of String, String),
        String
    ) =
        Function(fields)
            If fields Is Nothing Then
                Return ""
            End If
            Return String.Join(
                "&",
                fields.Select(
                    Function(kvp)
                        Return System.Net.WebUtility.UrlEncode(kvp.Key) &
                               "=" &
                               System.Net.WebUtility.UrlEncode(If(kvp.Value, ""))
                    End Function
                )
            )
        End Function


    Dim GetAttr As Func(Of String, String, String) =
        Function(tag As String, attrName As String) As String
            If String.IsNullOrWhiteSpace(tag) Then
                Return ""
            End If
            Dim pattern As String =
                attrName &
                "\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))"
            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    tag,
                    pattern,
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


    Dim ExtractInputs As Func(
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

            Dim inputMatches As System.Text.RegularExpressions.MatchCollection =
                System.Text.RegularExpressions.Regex.Matches(
                    html,
                    "<input\b[^>]*>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            For Each m As System.Text.RegularExpressions.Match In inputMatches
                Dim tag As String = m.Value
                Dim name As String = GetAttr(tag, "name")
                If Not String.IsNullOrWhiteSpace(name) Then
                    result(name) = GetAttr(tag, "value")
                End If
            Next

            Dim textareaMatches As System.Text.RegularExpressions.MatchCollection =
                System.Text.RegularExpressions.Regex.Matches(
                    html,
                    "<textarea\b[^>]*name\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>(.*?)</textarea>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            For Each m As System.Text.RegularExpressions.Match In textareaMatches
                Dim name As String = ""
                If m.Groups(1).Success Then
                    name = m.Groups(1).Value
                ElseIf m.Groups(2).Success Then
                    name = m.Groups(2).Value
                ElseIf m.Groups(3).Success Then
                    name = m.Groups(3).Value
                End If
                If Not String.IsNullOrWhiteSpace(name) Then
                    result(name) =
                        System.Net.WebUtility.HtmlDecode(m.Groups(4).Value)
                End If
            Next

            Return result
        End Function


    Dim GetFirstFormAction As Func(Of String, String, String) =
        Function(html As String, pageUrl As String) As String
            If String.IsNullOrWhiteSpace(html) Then
                Return NormalizeUrl(pageUrl)
            End If

            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "<form\b[^>]*>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            If Not m.Success Then
                Return NormalizeUrl(pageUrl)
            End If

            Dim action As String = GetAttr(m.Value, "action")
            If String.IsNullOrWhiteSpace(action) Then
                Return NormalizeUrl(pageUrl)
            End If

            Return NormalizeUrl(
                New System.Uri(New System.Uri(pageUrl), action).ToString()
            )
        End Function


    Dim NormalizeSp2Url As Func(Of String, String) =
        Function(url As String) As String
            If String.IsNullOrWhiteSpace(url) Then
                Return ""
            End If
            Dim n As String = NormalizeUrl(url)
            ' Cookie path + capture use /CMS/SP2/ (capital CMS).
            n = System.Text.RegularExpressions.Regex.Replace(
                n,
                "https://webapps\.illinois\.gov/cms/SP2",
                "https://webapps.illinois.gov/CMS/SP2",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )
            Return n
        End Function


    Dim FindForm As Func(Of String, String(), String) =
        Function(html As String, requiredFields As String()) As String
            If String.IsNullOrWhiteSpace(html) Then
                Return ""
            End If

            Dim forms As System.Text.RegularExpressions.MatchCollection =
                System.Text.RegularExpressions.Regex.Matches(
                    html,
                    "<form\b[^>]*>.*?</form>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            For Each fm As System.Text.RegularExpressions.Match In forms
                Dim formHtml As String = fm.Value
                Dim fields = ExtractInputs(formHtml)
                Dim allFound As Boolean = True
                For Each required As String In requiredFields
                    If Not fields.ContainsKey(required) Then
                        allFound = False
                        Exit For
                    End If
                Next
                If allFound Then
                    Return formHtml
                End If
            Next

            Return ""
        End Function


    Dim GetFormAction As Func(Of String, String, String) =
        Function(formHtml As String, pageUrl As String) As String
            If String.IsNullOrWhiteSpace(formHtml) Then
                Return NormalizeUrl(pageUrl)
            End If

            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    formHtml,
                    "<form\b[^>]*>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            If Not m.Success Then
                Return NormalizeUrl(pageUrl)
            End If

            Dim action As String = GetAttr(m.Value, "action")
            If String.IsNullOrWhiteSpace(action) Then
                Return NormalizeUrl(pageUrl)
            End If

            Return NormalizeUrl(
                New System.Uri(New System.Uri(pageUrl), action).ToString()
            )
        End Function


    Dim ExtractNamedField As Func(Of String, String, String) =
        Function(html As String, fieldName As String) As String
            If String.IsNullOrWhiteSpace(html) OrElse String.IsNullOrWhiteSpace(fieldName) Then
                Return ""
            End If

            Dim escapedName As String =
                System.Text.RegularExpressions.Regex.Escape(fieldName)

            ' textarea name=field
            Dim ta As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "<textarea\b[^>]*\bname\s*=\s*(?:""" & escapedName & """|'" & escapedName & "'|" & escapedName & ")[^>]*>(.*?)</textarea>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            If ta.Success Then
                Return System.Net.WebUtility.HtmlDecode(ta.Groups(1).Value)
            End If

            ' input name= then value=
            Dim inp1 As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "<input\b[^>]*\bname\s*=\s*(?:""" & escapedName & """|'" & escapedName & "'|" & escapedName & ")[^>]*\bvalue\s*=\s*(?:""([^""]*)""|'([^']*)')",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            If inp1.Success Then
                If inp1.Groups(1).Success Then
                    Return System.Net.WebUtility.HtmlDecode(inp1.Groups(1).Value)
                End If
                If inp1.Groups(2).Success Then
                    Return System.Net.WebUtility.HtmlDecode(inp1.Groups(2).Value)
                End If
            End If

            ' input value= then name=
            Dim inp2 As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "<input\b[^>]*\bvalue\s*=\s*(?:""([^""]*)""|'([^']*)')[^>]*\bname\s*=\s*(?:""" & escapedName & """|'" & escapedName & "'|" & escapedName & ")",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            If inp2.Success Then
                If inp2.Groups(1).Success Then
                    Return System.Net.WebUtility.HtmlDecode(inp2.Groups(1).Value)
                End If
                If inp2.Groups(2).Success Then
                    Return System.Net.WebUtility.HtmlDecode(inp2.Groups(2).Value)
                End If
            End If

            Return ""
        End Function


    Dim ResolveTokenAction As Func(Of String, String, String) =
        Function(html As String, pageUrl As String) As String
            ' Prefer explicit form action when it targets a *different* host/app
            ' (ADFS Working pages always post cross-host).
            Dim action As String = ""

            Dim formHtml As String =
                FindForm(html, New String() {"wa", "wresult"})

            If Not String.IsNullOrWhiteSpace(formHtml) Then
                action = GetFormAction(formHtml, pageUrl)
            Else
                action = GetFirstFormAction(html, pageUrl)
            End If

            action = NormalizeSp2Url(action)

            Dim pageHost As String = ""
            Dim actionHost As String = ""
            Try
                pageHost = New System.Uri(pageUrl).Host
            Catch
            End Try
            Try
                actionHost = New System.Uri(action).Host
            Catch
            End Try

            Dim actionIsUseful As Boolean =
                Not String.IsNullOrWhiteSpace(action) AndAlso
                Not String.IsNullOrWhiteSpace(actionHost) AndAlso
                Not actionHost.Equals(pageHost, StringComparison.OrdinalIgnoreCase)

            If actionIsUseful Then
                Return action
            End If

            ' Capture defaults when action is missing / same-host (common scrape miss):
            '   fspartner  -> fsresource /adfs/ls/
            '   fsresource -> webapps /CMS/SP2/
            '   CMS/SP2    -> AgingCares app root
            If HostIs(pageUrl, partnerAdfsHost) Then
                Return "https://fsresource.illinois.gov/adfs/ls/"
            End If

            If HostIs(pageUrl, resourceAdfsHost) Then
                Return "https://webapps.illinois.gov/CMS/SP2/"
            End If

            If pageUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               pageUrl.IndexOf("/cms/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 Then

                Dim realm As String = GetQueryValue(pageUrl, "wtrealm")
                If Not String.IsNullOrWhiteSpace(realm) AndAlso
                   realm.IndexOf("AgingCares", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    Return NormalizeUrl(realm)
                End If

                Return "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/"
            End If

            If Not String.IsNullOrWhiteSpace(action) Then
                Return action
            End If

            Return NormalizeUrl(pageUrl)
        End Function


    Dim TryGetWsFedFields As Func(
        Of String,
        String,
        System.Tuple(Of System.Collections.Generic.Dictionary(Of String, String), String)
    ) =
        Function(html As String, pageUrl As String)

            Dim empty =
                New System.Tuple(Of System.Collections.Generic.Dictionary(Of String, String), String)(
                    Nothing,
                    ""
                )

            If String.IsNullOrWhiteSpace(html) Then
                Return empty
            End If

            Dim wresult As String = ExtractNamedField(html, "wresult")
            If String.IsNullOrWhiteSpace(wresult) Then
                ' Fallback to generic input scrape
                Dim allFields = ExtractInputs(html)
                If allFields.ContainsKey("wresult") Then
                    wresult = allFields("wresult")
                End If
            End If

            If String.IsNullOrWhiteSpace(wresult) Then
                Return empty
            End If

            Dim fields As New System.Collections.Generic.Dictionary(Of String, String)(
                StringComparer.OrdinalIgnoreCase
            )

            Dim wa As String = ExtractNamedField(html, "wa")
            Dim wctx As String = ExtractNamedField(html, "wctx")

            If String.IsNullOrWhiteSpace(wa) Then
                Dim allFields = ExtractInputs(html)
                If allFields.ContainsKey("wa") Then
                    wa = allFields("wa")
                End If
                If String.IsNullOrWhiteSpace(wctx) AndAlso allFields.ContainsKey("wctx") Then
                    wctx = allFields("wctx")
                End If
            End If

            If String.IsNullOrWhiteSpace(wa) Then
                wa = "wsignin1.0"
            End If

            fields("wa") = wa
            fields("wresult") = wresult
            If Not String.IsNullOrWhiteSpace(wctx) Then
                fields("wctx") = wctx
            End If

            Dim action As String = ResolveTokenAction(html, pageUrl)

            Return New System.Tuple(Of System.Collections.Generic.Dictionary(Of String, String), String)(
                fields,
                action
            )
        End Function


    Dim ExtractRedirectUrl As Func(Of String, String, String) =
        Function(html As String, baseUrl As String) As String
            If String.IsNullOrWhiteSpace(html) Then
                Return ""
            End If

            Dim normalized As String = System.Net.WebUtility.HtmlDecode(html)
            normalized = normalized.Replace("\u0026", "&")
            normalized = normalized.Replace("\u003a", ":")
            normalized = normalized.Replace("\u002f", "/")
            normalized = normalized.Replace("\/", "/")
            normalized = normalized.Replace("&amp;", "&")

            Dim jsMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    normalized,
                    "(?:window\.location|document\.location|location\.href|location\.replace)\s*(?:=|\()\s*['""]([^'""]+)['""]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            If jsMatch.Success Then
                Dim candidate As String = jsMatch.Groups(1).Value.Trim()
                If Not String.IsNullOrWhiteSpace(candidate) Then
                    Return NormalizeUrl(
                        New System.Uri(New System.Uri(baseUrl), candidate).ToString()
                    )
                End If
            End If

            Dim metaMatch As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    normalized,
                    "<meta[^>]+http-equiv\s*=\s*[""']?refresh[""']?[^>]+content\s*=\s*[""'][^""']*url\s*=\s*([^""']+)[""']",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase Or
                    System.Text.RegularExpressions.RegexOptions.Singleline
                )

            If metaMatch.Success Then
                Dim candidate As String = metaMatch.Groups(1).Value.Trim()
                If Not String.IsNullOrWhiteSpace(candidate) Then
                    Return NormalizeUrl(
                        New System.Uri(New System.Uri(baseUrl), candidate).ToString()
                    )
                End If
            End If

            Return ""
        End Function


    Dim IsFederationIdentifier As Func(Of String, Boolean) =
        Function(url As String) As Boolean
            ' ADFS "trust" URIs are realm IDs (often http://...), NOT pages to GET.
            If String.IsNullOrWhiteSpace(url) Then
                Return True
            End If
            If url.IndexOf("/adfs/services/trust", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If
            If url.IndexOf("/FederationMetadata/", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If
            Try
                Dim u As New System.Uri(url)
                If u.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) AndAlso
                   u.Host.IndexOf("illinois.gov", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    ' Illinois ADFS identifiers are http://host/adfs/services/trust
                    Return True
                End If
            Catch
            End Try
            Return False
        End Function


    Dim IsLoginEndpointUrl As Func(Of String, Boolean) =
        Function(url As String) As Boolean
            If String.IsNullOrWhiteSpace(url) OrElse IsFederationIdentifier(url) Then
                Return False
            End If
            Try
                Dim u As New System.Uri(url)
                If Not u.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) Then
                    Return False
                End If
                Return u.AbsolutePath.IndexOf("/adfs/ls", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                       u.AbsolutePath.IndexOf("/CMS/SP2", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                       u.AbsolutePath.IndexOf("/AGE/", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                       u.AbsolutePath.IndexOf("/cms/SP2", StringComparison.OrdinalIgnoreCase) >= 0
            Catch
                Return False
            End Try
        End Function


    Dim FindPartnerLink As Func(Of String, String, String) =
        Function(html As String, baseUrl As String) As String
            If String.IsNullOrWhiteSpace(html) Then
                Return ""
            End If

            Dim decoded As String =
                System.Net.WebUtility.HtmlDecode(html).Replace("&amp;", "&")

            ' Only real browseable fspartner login endpoints (/adfs/ls...), never
            ' http://fspartner.../adfs/services/trust (federation identifier).
            Dim patterns As String() = {
                "https://fspartner\.illinois\.gov/adfs/ls[^""'\s<>]*",
                "https://fspartner\.illinois\.gov:443/adfs/ls[^""'\s<>]*"
            }

            For Each pat As String In patterns
                Dim m As System.Text.RegularExpressions.Match =
                    System.Text.RegularExpressions.Regex.Match(
                        decoded,
                        pat,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                    )
                If m.Success Then
                    Dim candidate As String = m.Value.Trim()
                    Try
                        Dim absolute As String =
                            NormalizeUrl(
                                New System.Uri(New System.Uri(baseUrl), candidate).ToString()
                            )
                        If IsLoginEndpointUrl(absolute) Then
                            Return absolute
                        End If
                    Catch
                    End Try
                End If
            Next

            ' Href that already includes whr= pointing at partner, on fsresource /adfs/ls
            Dim whrHref As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    decoded,
                    "https://fsresource\.illinois\.gov[^""'\s<>]*whr=[^""'\s<>]*fspartner[^""'\s<>]*",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                )

            If whrHref.Success Then
                Dim absolute As String = NormalizeUrl(whrHref.Value.Trim())
                If IsLoginEndpointUrl(absolute) Then
                    Return absolute
                End If
            End If

            Return ""
        End Function


    Dim IsTransientNetworkError As Func(Of System.Exception, Boolean) =
        Function(ex As System.Exception) As Boolean
            Dim text As String = If(ex Is Nothing, "", ex.ToString())
            If text.IndexOf("forcibly closed", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If
            If text.IndexOf("10054", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If
            If text.IndexOf("connection was closed", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If
            If text.IndexOf("Unable to read data from the transport connection", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If
            If text.IndexOf("The underlying connection was closed", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Return True
            End If
            Return False
        End Function


    Dim SendRequest As Func(
        Of String,
        String,
        System.Collections.Generic.Dictionary(Of String, String),
        String,
        System.Tuple(Of String, String, Integer, String)
    ) =
        Function(
            method As String,
            url As String,
            fields As System.Collections.Generic.Dictionary(Of String, String),
            referer As String
        )

            url = NormalizeUrl(url)

            If IsFederationIdentifier(url) Then
                Throw New System.Exception(
                    "Refusing to GET federation identifier (not a login page): " & url &
                    ". Use /adfs/ls with whr= query, not /adfs/services/trust."
                )
            End If

            Dim lastEx As System.Exception = Nothing

            For attempt As Integer = 1 To 4

                Dim req As System.Net.HttpWebRequest =
                    CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)

                req.Method = method
                req.CookieContainer = cookieJar
                req.AllowAutoRedirect = False
                req.ProtocolVersion = System.Net.HttpVersion.Version11

                ' Fresh TCP each request — reused Keep-Alive sockets often get RST
                ' by ADFS / edge (Akamai) on fspartner.
                req.KeepAlive = False

                req.Timeout = 120000
                req.ReadWriteTimeout = 120000

                req.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " &
                    "AppleWebKit/537.36 (KHTML, like Gecko) " &
                    "Chrome/153.0.0.0 Safari/537.36"

                req.Accept =
                    "text/html,application/xhtml+xml,application/xml;q=0.9," &
                    "image/avif,image/webp,image/apng,*/*;q=0.8"

                req.Headers(System.Net.HttpRequestHeader.AcceptLanguage) = "en-US,en;q=0.9"
                req.Headers(System.Net.HttpRequestHeader.CacheControl) = "no-cache"
                req.Headers(System.Net.HttpRequestHeader.Pragma) = "no-cache"

                Try
                    req.Headers("Upgrade-Insecure-Requests") = "1"
                    req.Headers("sec-ch-ua") =
                        """Google Chrome"";v=""153"", ""Not:A-Brand"";v=""8"", ""Chromium"";v=""153"""
                    req.Headers("sec-ch-ua-mobile") = "?0"
                    req.Headers("sec-ch-ua-platform") = """Windows"""
                    req.Headers("sec-fetch-dest") = "document"
                    req.Headers("sec-fetch-mode") = "navigate"
                    req.Headers("sec-fetch-user") = "?1"

                    Dim fetchSite As String = "none"
                    If Not String.IsNullOrWhiteSpace(referer) Then
                        Try
                            If New System.Uri(NormalizeUrl(referer)).Host.Equals(
                                New System.Uri(url).Host,
                                StringComparison.OrdinalIgnoreCase
                            ) Then
                                fetchSite = "same-origin"
                            Else
                                fetchSite = "cross-site"
                            End If
                        Catch
                            fetchSite = "cross-site"
                        End Try
                    End If
                    req.Headers("sec-fetch-site") = fetchSite
                Catch
                    ' Some hosts reject unknown headers; optional.
                End Try

                req.AutomaticDecompression =
                    System.Net.DecompressionMethods.GZip Or
                    System.Net.DecompressionMethods.Deflate

                If Not String.IsNullOrWhiteSpace(referer) Then
                    req.Referer = NormalizeUrl(referer)
                    If method.Equals("POST", StringComparison.OrdinalIgnoreCase) Then
                        Try
                            Dim refUri As New System.Uri(NormalizeUrl(referer))
                            req.Headers("Origin") =
                                refUri.GetLeftPart(System.UriPartial.Authority)
                        Catch
                        End Try
                    End If
                End If

                Try
                    If method.Equals("POST", StringComparison.OrdinalIgnoreCase) Then
                        Dim body As String = BuildFormBody(fields)
                        Dim bodyBytes As Byte() =
                            System.Text.Encoding.UTF8.GetBytes(body)

                        req.ContentType = "application/x-www-form-urlencoded"
                        req.ContentLength = bodyBytes.Length

                        Using stream As System.IO.Stream = req.GetRequestStream()
                            stream.Write(bodyBytes, 0, bodyBytes.Length)
                        End Using
                    End If

                    Console.WriteLine(">>> " & method & " " & url)

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
                        Dim responseText As String = ""
                        If response.GetResponseStream() IsNot Nothing Then
                            Using reader As New System.IO.StreamReader(
                                response.GetResponseStream()
                            )
                                responseText = reader.ReadToEnd()
                            End Using
                        End If

                        Dim location As String = response.Headers("Location")
                        If location Is Nothing Then
                            location = ""
                        End If

                        Dim responseUrl As String =
                            NormalizeUrl(response.ResponseUri.ToString())
                        Dim status As Integer = CInt(response.StatusCode)

                        Dim fieldNames As String = ""
                        If fields IsNot Nothing Then
                            fieldNames = String.Join(",", fields.Keys)
                        End If

                        Dim locAbsolute As String = ""
                        If Not String.IsNullOrWhiteSpace(location) Then
                            locAbsolute =
                                NormalizeUrl(
                                    New System.Uri(
                                        New System.Uri(responseUrl),
                                        location
                                    ).ToString()
                                )
                        End If

                        Console.WriteLine(
                            method & " " & url & " -> HTTP " & status.ToString()
                        )

                        loginTrace.Add(
                            method & " | " & url &
                            " | Status=" & status.ToString() &
                            If(
                                String.IsNullOrWhiteSpace(locAbsolute),
                                "",
                                " | Location=" & locAbsolute
                            ) &
                            If(
                                String.IsNullOrWhiteSpace(fieldNames),
                                "",
                                " | Fields=" & fieldNames
                            ) &
                            If(attempt > 1, " | Retry=" & attempt.ToString(), "")
                        )

                        Return New System.Tuple(Of String, String, Integer, String)(
                            responseText,
                            responseUrl,
                            status,
                            locAbsolute
                        )
                    End Using

                Catch ex As System.Exception
                    lastEx = ex

                    If attempt < 4 AndAlso IsTransientNetworkError(ex) Then
                        Console.WriteLine(
                            "Transient network error on attempt " &
                            attempt.ToString() &
                            " — retrying " & method & " " & url
                        )
                        loginTrace.Add(
                            "RETRY | attempt=" & attempt.ToString() &
                            " | " & method & " | " & url &
                            " | " & ex.Message
                        )
                        System.Threading.Thread.Sleep(400 * attempt)
                        Continue For
                    End If

                    Throw
                End Try

            Next

            If lastEx IsNot Nothing Then
                Throw lastEx
            End If

            Throw New System.Exception("SendRequest failed with no response: " & url)

        End Function


    Dim FollowRedirectGet As Func(Of String, String, System.Tuple(Of String, String, Integer, String)) =
        Function(redirectUrl As String, referer As String)
            redirectUrl = NormalizeUrl(redirectUrl)
            Dim isWebApps As Boolean =
                redirectUrl.IndexOf("webapps.illinois.gov", StringComparison.OrdinalIgnoreCase) >= 0
            Dim attempts As Integer = If(isWebApps, 4, 1)
            Dim lastEx As System.Exception = Nothing

            For attempt As Integer = 1 To attempts
                If attempt > 1 Then
                    Console.WriteLine(
                        "Redirect GET retry " & attempt.ToString() &
                        " | " & redirectUrl
                    )
                    loginTrace.Add(
                        "REDIRECT RETRY | attempt=" & attempt.ToString() &
                        " | " & redirectUrl
                    )
                    System.Threading.Thread.Sleep(800 * attempt)
                End If

                If isWebApps AndAlso attempt = 1 Then
                    LogCookies("before webapps redirect GET")
                End If

                Try
                    Return SendRequest("GET", redirectUrl, Nothing, referer)
                Catch ex As System.Exception
                    lastEx = ex
                    Dim retryable As Boolean =
                        IsTransientNetworkError(ex) OrElse
                        TypeOf ex Is System.Net.WebException
                    If Not retryable OrElse attempt >= attempts Then
                        Throw
                    End If
                End Try
            Next

            If lastEx IsNot Nothing Then
                Throw lastEx
            End If

            Throw New System.Exception("FollowRedirectGet failed: " & redirectUrl)
        End Function


    Dim current =
        SendRequest("GET", startUrl, Nothing, "")

    Dim currentHtml As String = ""
    Dim loggedIn As Boolean = False
    Dim realmPosted As Boolean = False
    Dim partnerDirectDone As Boolean = False
    Dim sp2SettleTries As Integer = 0

    currentUrl = ""
    currentStatus = 0
    currentLocation = ""
    loginPosted = False
    partnerJumpDone = False
    tokenPosts = 0


    For stepNumber As Integer = 1 To 60

        currentHtml = If(current.Item1, "")
        currentUrl = If(current.Item2, "")
        currentStatus = current.Item3
        currentLocation = If(current.Item4, "")


        ' --------------------------------------------------------
        ' SUCCESS: AgingCares app page with HTTP 200 (no token form)
        ' --------------------------------------------------------
        If currentStatus = 200 AndAlso IsAgingCaresAppUrl(currentUrl) Then
            Dim stillTokenForm As String =
                FindForm(currentHtml, New String() {"wa", "wresult", "wctx"})

            If String.IsNullOrWhiteSpace(stillTokenForm) Then
                loggedIn = True
                Console.WriteLine("AgingCares application reached.")
                loginTrace.Add("SUCCESS | Protected application reached | " & currentUrl)
                Exit For
            End If
        End If


        ' --------------------------------------------------------
        ' WS-Fed token auto-post — before redirect follow (302 can include wresult body)
        ' --------------------------------------------------------
        Dim wsFed = TryGetWsFedFields(currentHtml, currentUrl)

        If wsFed.Item1 IsNot Nothing AndAlso
           Not String.IsNullOrWhiteSpace(wsFed.Item2) Then

            Dim tokenFields = wsFed.Item1
            Dim tokenAction As String = wsFed.Item2

            tokenPosts += 1
            Console.WriteLine(
                "POST WS-Fed token handoff #" & tokenPosts.ToString() &
                " -> " & tokenAction
            )
            loginTrace.Add(
                "TOKEN HANDOFF #" & tokenPosts.ToString() &
                " | POST " & tokenAction &
                " | from=" & currentUrl &
                " | wresultChars=" & tokenFields("wresult").Length.ToString()
            )

            Dim previousUrl As String = currentUrl
            current = SendRequest("POST", tokenAction, tokenFields, previousUrl)
            If tokenPosts >= 2 Then
                LogCookies("after token handoff #" & tokenPosts.ToString())
            End If
            Continue For
        End If


        ' --------------------------------------------------------
        ' HTTP redirect
        ' --------------------------------------------------------
        If currentStatus >= 300 AndAlso
           currentStatus <= 399 AndAlso
           Not String.IsNullOrWhiteSpace(currentLocation) Then

            If IsFederationIdentifier(currentLocation) Then
                loginTrace.Add("SKIP REDIRECT | federation id | " & currentLocation)
            Else
                Console.WriteLine("FOLLOW REDIRECT -> GET " & currentLocation)
                loginTrace.Add("FOLLOW REDIRECT | " & currentLocation)

                Dim previousUrl As String = currentUrl
                current = FollowRedirectGet(currentLocation, previousUrl)
                If currentLocation.IndexOf("webapps.illinois.gov", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    LogCookies("after webapps redirect GET")
                End If
                Continue For
            End If
        End If


        ' --------------------------------------------------------
        ' ADFS username/password form (capture: DOMAIN\user on fspartner)
        ' Match form OR whole-page inputs if </form> is missing.
        ' --------------------------------------------------------
        Dim loginForm As String =
            FindForm(currentHtml, New String() {"UserName", "Password"})

        If String.IsNullOrWhiteSpace(loginForm) Then
            loginForm = FindForm(currentHtml, New String() {"UserName"})
        End If

        Dim pageFields = ExtractInputs(currentHtml)
        Dim hasPasswordFields As Boolean =
            pageFields.ContainsKey("UserName") AndAlso
            pageFields.ContainsKey("Password")

        If Not loginPosted AndAlso
           (Not String.IsNullOrWhiteSpace(loginForm) OrElse hasPasswordFields) Then

            Dim loginFields As System.Collections.Generic.Dictionary(Of String, String)

            If Not String.IsNullOrWhiteSpace(loginForm) Then
                loginFields = ExtractInputs(loginForm)
            Else
                loginFields = pageFields
            End If

            Dim loginAction As String =
                If(
                    Not String.IsNullOrWhiteSpace(loginForm),
                    GetFormAction(loginForm, currentUrl),
                    NormalizeUrl(currentUrl)
                )

            ' Capture uses DOMAIN\user exactly — do not rewrite for password POST.
            loginFields("UserName") = appUser
            loginFields("Password") = appPasswordPlain
            loginFields("AuthMethod") = "FormsAuthentication"

            Console.WriteLine("POST ADFS login form")
            loginTrace.Add("LOGIN FORM | POST " & loginAction)

            Dim previousUrl As String = currentUrl
            current = SendRequest("POST", loginAction, loginFields, previousUrl)
            loginPosted = True
            Continue For
        End If


        ' --------------------------------------------------------
        ' Optional home-realm email page (ONCE only)
        ' --------------------------------------------------------
        Dim realmForm As String =
            FindForm(currentHtml, New String() {"Email", "HomeRealmByEmail"})

        If Not realmPosted AndAlso Not String.IsNullOrWhiteSpace(realmForm) Then

            Dim realmFields = ExtractInputs(realmForm)
            Dim realmAction As String = GetFormAction(realmForm, currentUrl)

            realmFields("Email") = ToRealmEmail(appUser)
            realmFields("HomeRealmByEmail") = "true"

            Console.WriteLine("POST home-realm form")
            loginTrace.Add(
                "HOME REALM | POST " & realmAction &
                " | Email=" & ToRealmEmail(appUser)
            )

            Dim previousUrl As String = currentUrl
            current = SendRequest("POST", realmAction, realmFields, previousUrl)
            realmPosted = True
            Continue For
        End If


        ' --------------------------------------------------------
        ' SP2 → AgingCares handoff page often needs several GETs
        ' before the token form appears (see capture req 6-8).
        ' wtrealm=AgingCares is usually in query string, not path.
        ' --------------------------------------------------------
        If currentStatus = 200 AndAlso
           currentUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
           tokenPosts >= 2 AndAlso
           Not loggedIn AndAlso
           sp2SettleTries < 5 AndAlso
           (currentUrl.IndexOf("wa=", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            currentUrl.IndexOf("AgingCares", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            currentHtml.IndexOf("wresult", StringComparison.OrdinalIgnoreCase) < 0) Then

            sp2SettleTries += 1
            Console.WriteLine(
                "SP2 settle re-GET #" & sp2SettleTries.ToString()
            )
            loginTrace.Add("SP2 SETTLE | GET " & currentUrl & " | try=" & sp2SettleTries.ToString())

            System.Threading.Thread.Sleep(300)

            Dim previousUrl As String = currentUrl
            current = SendRequest("GET", currentUrl, Nothing, previousUrl)
            Continue For
        End If


        ' --------------------------------------------------------
        ' JS / meta redirect
        ' --------------------------------------------------------
        Dim jsRedirect As String = ExtractRedirectUrl(currentHtml, currentUrl)

        If Not String.IsNullOrWhiteSpace(jsRedirect) AndAlso
           Not IsFederationIdentifier(jsRedirect) AndAlso
           Not jsRedirect.Equals(currentUrl, StringComparison.OrdinalIgnoreCase) Then

            Console.WriteLine("FOLLOW HTML REDIRECT")
            loginTrace.Add("HTML REDIRECT | GET " & jsRedirect)

            Dim previousUrl As String = currentUrl
            current = SendRequest("GET", jsRedirect, Nothing, previousUrl)
            Continue For
        End If


        ' --------------------------------------------------------
        ' Partner IdP link on HRD page
        ' --------------------------------------------------------
        Dim partnerLink As String = FindPartnerLink(currentHtml, currentUrl)

        If Not String.IsNullOrWhiteSpace(partnerLink) AndAlso
           IsLoginEndpointUrl(partnerLink) AndAlso
           Not partnerLink.Equals(currentUrl, StringComparison.OrdinalIgnoreCase) Then

            Console.WriteLine("FOLLOW PARTNER IDP LINK")
            loginTrace.Add("PARTNER LINK | GET " & partnerLink)

            Dim previousUrl As String = currentUrl
            current = SendRequest("GET", partnerLink, Nothing, previousUrl)
            Continue For
        End If


        ' --------------------------------------------------------
        ' Still on fsresource without password form → whr select,
        ' then direct fspartner login URL if still stuck.
        ' --------------------------------------------------------
        If Not partnerJumpDone AndAlso
           Not loginPosted AndAlso
           currentStatus = 200 AndAlso
           IsAdfsUrl(currentUrl) AndAlso
           HostIs(currentUrl, resourceAdfsHost) Then

            partnerJumpDone = True

            Dim previousUrl As String = currentUrl
            Dim whrUrl As String = BuildWhrSelectUrl(currentUrl)

            Console.WriteLine("SELECT PARTNER VIA whr")
            loginTrace.Add("WHR SELECT | GET " & whrUrl)

            Try
                current = SendRequest("GET", whrUrl, Nothing, previousUrl)
            Catch whrEx As System.Exception
                If Not IsTransientNetworkError(whrEx) Then
                    Throw
                End If

                Dim partnerUrl As String = BuildPartnerLoginUrl(currentUrl)
                Console.WriteLine("WHR failed; fallback direct fspartner")
                loginTrace.Add(
                    "PARTNER JUMP FALLBACK | GET " & partnerUrl &
                    " | after=" & whrEx.Message
                )
                current = SendRequest("GET", partnerUrl, Nothing, previousUrl)
            End Try

            Continue For
        End If


        ' After whr, still on fsresource with no login form → force partner /adfs/ls
        If loginPosted = False AndAlso
           partnerJumpDone AndAlso
           Not partnerDirectDone AndAlso
           currentStatus = 200 AndAlso
           IsAdfsUrl(currentUrl) AndAlso
           HostIs(currentUrl, resourceAdfsHost) AndAlso
           Not pageFields.ContainsKey("UserName") Then

            partnerDirectDone = True
            Dim partnerUrl As String = BuildPartnerLoginUrl(currentUrl)
            Console.WriteLine("DIRECT FSPARTNER LOGIN URL")
            loginTrace.Add("PARTNER DIRECT | GET " & partnerUrl)

            Dim previousUrl As String = currentUrl
            current = SendRequest("GET", partnerUrl, Nothing, previousUrl)
            Continue For
        End If


        ' Debug: list field names seen when stuck
        Dim stuckFields As String = String.Join(",", pageFields.Keys)
        Dim hasWresultText As Boolean =
            currentHtml.IndexOf("wresult", StringComparison.OrdinalIgnoreCase) >= 0

        loginTrace.Add(
            "STOP | No recognised auth action | Url=" & currentUrl &
            " | Fields=" & stuckFields &
            " | HtmlHasWresultText=" & hasWresultText.ToString() &
            " | HtmlLen=" & currentHtml.Length.ToString() &
            " | LoginPosted=" & loginPosted.ToString() &
            " | TokenPosts=" & tokenPosts.ToString()
        )
        Console.WriteLine("Auth loop stopped; running final verification.")
        Exit For

    Next


    ' ============================================================
    ' FINAL VERIFICATION — drain current page first, then protected URL
    ' ============================================================
    If Not loggedIn Then

        ' Do NOT throw away a pending WS-Fed form on the last response.
        Dim pending = TryGetWsFedFields(currentHtml, currentUrl)
        If pending.Item1 IsNot Nothing AndAlso
           Not String.IsNullOrWhiteSpace(pending.Item2) Then

            Console.WriteLine("FINAL | posting pending WS-Fed form first")
            loginTrace.Add("FINAL PENDING TOKEN | POST " & pending.Item2)

            Dim pendingPosted =
                SendRequest("POST", pending.Item2, pending.Item1, currentUrl)

            tokenPosts += 1
            currentHtml = If(pendingPosted.Item1, "")
            currentUrl = If(pendingPosted.Item2, "")
            currentStatus = pendingPosted.Item3
            currentLocation = If(pendingPosted.Item4, "")

            If currentStatus = 200 AndAlso IsAgingCaresAppUrl(currentUrl) Then
                Dim still = TryGetWsFedFields(currentHtml, currentUrl)
                If still.Item1 Is Nothing Then
                    loggedIn = True
                End If
            End If
        End If

    End If

    If Not loggedIn Then

        Console.WriteLine("FINAL TEST | GET protected URL and follow redirects")
        loginTrace.Add("FINAL TEST | GET protected URL")

        Dim verifyUrl As String = startUrl
        Dim previousUrl As String = currentUrl

        ' If SP2 session cookies exist, hit SP2 AgingCares handoff first.
        Try
            Dim webCookies As System.Net.CookieCollection =
                cookieJar.GetCookies(New System.Uri("https://webapps.illinois.gov/CMS/SP2/"))

            Dim hasSpAuth As Boolean = False
            For Each c As System.Net.Cookie In webCookies
                If c.Name.Equals("SPAuth", StringComparison.OrdinalIgnoreCase) OrElse
                   c.Name.Equals("SPAuth1", StringComparison.OrdinalIgnoreCase) Then
                    hasSpAuth = True
                    Exit For
                End If
            Next

            If hasSpAuth Then
                verifyUrl =
                    "https://webapps.illinois.gov/CMS/SP2/?wa=wsignin1.0&wtrealm=" &
                    System.Uri.EscapeDataString(
                        "https://webapps.illinois.gov/AGE/AgingCares.CaseManagement/"
                    ) &
                    "&wctx=" &
                    System.Uri.EscapeDataString(
                        "rm=0&id=passive&ru=" &
                        System.Uri.EscapeDataString(
                            "/AGE/AgingCares.CaseManagement/CMIS/CMIS/ProvidersIndex"
                        )
                    )
                loginTrace.Add("FINAL TEST | SP2 handoff first | " & verifyUrl)
            End If
        Catch
        End Try

        For hop As Integer = 1 To 25

            Dim finalTest =
                SendRequest("GET", verifyUrl, Nothing, previousUrl)

            currentHtml = If(finalTest.Item1, "")
            currentUrl = If(finalTest.Item2, "")
            currentStatus = finalTest.Item3
            currentLocation = If(finalTest.Item4, "")
            previousUrl = currentUrl

            If currentStatus = 200 AndAlso IsAgingCaresAppUrl(currentUrl) Then
                Dim leftover = TryGetWsFedFields(currentHtml, currentUrl)

                If leftover.Item1 Is Nothing Then
                    loggedIn = True
                    Exit For
                End If

                Dim posted =
                    SendRequest("POST", leftover.Item2, leftover.Item1, previousUrl)
                tokenPosts += 1

                currentHtml = If(posted.Item1, "")
                currentUrl = If(posted.Item2, "")
                currentStatus = posted.Item3
                currentLocation = If(posted.Item4, "")
                previousUrl = currentUrl

                If currentStatus >= 300 AndAlso
                   currentStatus <= 399 AndAlso
                   Not String.IsNullOrWhiteSpace(currentLocation) AndAlso
                   Not IsFederationIdentifier(currentLocation) Then
                    verifyUrl = currentLocation
                    Continue For
                End If

                verifyUrl = currentUrl
                Continue For
            End If

            If currentStatus >= 300 AndAlso
               currentStatus <= 399 AndAlso
               Not String.IsNullOrWhiteSpace(currentLocation) AndAlso
               Not IsFederationIdentifier(currentLocation) Then
                verifyUrl = currentLocation
                Continue For
            End If

            ' Any remaining WS-Fed form during verify
            Dim verifyWsFed = TryGetWsFedFields(currentHtml, currentUrl)
            If verifyWsFed.Item1 IsNot Nothing Then
                Dim posted =
                    SendRequest("POST", verifyWsFed.Item2, verifyWsFed.Item1, previousUrl)
                tokenPosts += 1
                currentHtml = If(posted.Item1, "")
                currentUrl = If(posted.Item2, "")
                currentStatus = posted.Item3
                currentLocation = If(posted.Item4, "")
                previousUrl = currentUrl

                If currentStatus >= 300 AndAlso
                   currentStatus <= 399 AndAlso
                   Not String.IsNullOrWhiteSpace(currentLocation) AndAlso
                   Not IsFederationIdentifier(currentLocation) Then
                    verifyUrl = currentLocation
                    Continue For
                End If

                verifyUrl = currentUrl
                Continue For
            End If

            ' SP2 AgingCares settle during verify
            If currentStatus = 200 AndAlso
               currentUrl.IndexOf("/CMS/SP2/", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
               currentUrl.IndexOf("AgingCares", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
               sp2SettleTries < 5 Then

                sp2SettleTries += 1
                System.Threading.Thread.Sleep(300)
                verifyUrl = currentUrl
                Continue For
            End If

            ' If final test landed on ADFS login and we never posted credentials, try once.
            If Not loginPosted AndAlso currentStatus = 200 AndAlso IsAdfsUrl(currentUrl) Then
                Dim lateLogin =
                    FindForm(currentHtml, New String() {"UserName", "Password"})

                If String.IsNullOrWhiteSpace(lateLogin) Then
                    lateLogin = FindForm(currentHtml, New String() {"UserName"})
                End If

                Dim lateFields = ExtractInputs(currentHtml)

                If Not String.IsNullOrWhiteSpace(lateLogin) OrElse
                   (lateFields.ContainsKey("UserName") AndAlso lateFields.ContainsKey("Password")) Then

                    Dim loginFields =
                        If(
                            Not String.IsNullOrWhiteSpace(lateLogin),
                            ExtractInputs(lateLogin),
                            lateFields
                        )

                    Dim loginAction =
                        If(
                            Not String.IsNullOrWhiteSpace(lateLogin),
                            GetFormAction(lateLogin, currentUrl),
                            NormalizeUrl(currentUrl)
                        )

                    loginFields("UserName") = appUser
                    loginFields("Password") = appPasswordPlain
                    loginFields("AuthMethod") = "FormsAuthentication"

                    Dim posted =
                        SendRequest("POST", loginAction, loginFields, previousUrl)

                    loginPosted = True
                    currentHtml = If(posted.Item1, "")
                    currentUrl = If(posted.Item2, "")
                    currentStatus = posted.Item3
                    currentLocation = If(posted.Item4, "")
                    previousUrl = currentUrl

                    If currentStatus >= 300 AndAlso
                       currentStatus <= 399 AndAlso
                       Not String.IsNullOrWhiteSpace(currentLocation) AndAlso
                       Not IsFederationIdentifier(currentLocation) Then
                        verifyUrl = currentLocation
                        Continue For
                    End If

                    verifyUrl = currentUrl
                    Continue For
                End If

                If HostIs(currentUrl, resourceAdfsHost) AndAlso Not partnerJumpDone Then
                    partnerJumpDone = True
                    verifyUrl = BuildWhrSelectUrl(currentUrl)
                    Continue For
                End If
            End If

            Exit For
        Next

        If loggedIn Then
            Console.WriteLine("AgingCares application reached after final verification.")
            loginTrace.Add("SUCCESS | Final verification reached app | " & currentUrl)
        End If

    End If


    Dim cookieNamesCsv As String = CollectCookieNames()


    sessionData("FinalUrl") = currentUrl
    sessionData("StatusCode") = currentStatus
    sessionData("IsLoggedIn") = loggedIn
    sessionData("Trace") = String.Join(Environment.NewLine, loginTrace)
    sessionData("CookieNames") = cookieNamesCsv
    sessionData("TokenPosts") = tokenPosts
    sessionData("LoginPosted") = loginPosted
    sessionData("PartnerJump") = partnerJumpDone
    sessionData("PendingRedirect") = If(currentLocation, "")

    cookies = cookieJar


    If Not loggedIn Then

        errorMessage =
            "Login did not reach AgingCares. FinalUrl: " &
            currentUrl &
            Environment.NewLine &
            "HTTP Status: " &
            currentStatus.ToString() &
            Environment.NewLine &
            "LoginPosted=" & loginPosted.ToString() &
            ", TokenPosts=" & tokenPosts.ToString() &
            ", PartnerJump=" & partnerJumpDone.ToString() &
            Environment.NewLine &
            "CookieNames=" & cookieNamesCsv &
            Environment.NewLine &
            "Expected token chain: fspartner→fsresource, fsresource→CMS/SP2, SP2→AgingCares." &
            " Check sessionData Trace for TOKEN HANDOFF targets."

        Console.WriteLine("AgingCares login failed.")

    Else

        Console.WriteLine("AgingCares login completed successfully.")

    End If


Catch ex As Exception

    If sessionData Is Nothing Then
        sessionData =
            New System.Collections.Generic.Dictionary(Of String, Object)(
                StringComparer.OrdinalIgnoreCase
            )
    End If

    sessionData("IsLoggedIn") = False
    sessionData("FinalUrl") = If(currentUrl, "")
    sessionData("StatusCode") = currentStatus
    sessionData("Trace") = String.Join(Environment.NewLine, loginTrace)
    sessionData("TokenPosts") = tokenPosts
    sessionData("LoginPosted") = loginPosted
    sessionData("PartnerJump") = partnerJumpDone
    sessionData("PendingRedirect") = If(currentLocation, "")

    If cookieJar IsNot Nothing Then
        cookies = cookieJar
    End If
    sessionData("CookieNames") = CookieNamesFromJar(cookieJar)

    errorMessage = ex.ToString()
    Console.WriteLine("AgingCares login failed.")
    If Not String.IsNullOrWhiteSpace(currentLocation) Then
        Console.WriteLine("Last redirect Location (may have timed out): " & currentLocation)
    End If
    Console.WriteLine(
        "Failure context | FinalUrl=" & If(currentUrl, "") &
        " | HTTP=" & currentStatus.ToString() &
        " | TokenPosts=" & tokenPosts.ToString()
    )

Finally

    appPasswordPlain = Nothing

End Try
