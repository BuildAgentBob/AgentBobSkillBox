Try
    errorMessage = Nothing
    Dim pageHtml As String = Nothing
    payers = New Dictionary(Of String, Object)

    Dim url As String = "https://homecare1.axxessweb.com/Billing/Managed/Claims/Create"

    Dim request = CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)
    request.Method = "GET"
    request.CookieContainer = cookies
    request.Accept = "text/html, */*; q=0.01"
    request.UserAgent = "Mozilla/5.0"
    request.Referer = "https://homecare1.axxessweb.com/"
    request.Headers.Add("x-requested-with", "XMLHttpRequest")
    request.Headers.Add("x-build-date-identifier",  buildDateIdentifier)
    request.Headers.Add("x-culturecode", "en-US")
    request.Headers.Add("Cache-Control", "no-cache")
    request.Headers.Add("Pragma", "no-cache")

    Using response = CType(request.GetResponse(), System.Net.HttpWebResponse)
        Using reader As New System.IO.StreamReader(response.GetResponseStream())
            pageHtml = reader.ReadToEnd()
        End Using
    End Using

    Dim payerSelect = System.Text.RegularExpressions.Regex.Match(
        pageHtml,
        "<select[^>]*id=['""]NewManagedClaims_InsuranceId['""][\s\S]*?</select>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)

    If Not payerSelect.Success Then
        Throw New Exception("Payment Source dropdown not found.")
    End If

    Dim payerOptions = System.Text.RegularExpressions.Regex.Matches(
        payerSelect.Value,
        "<option\s+value=['""]([^'""]+)['""][^>]*>(.*?)</option>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)

    For Each m As System.Text.RegularExpressions.Match In payerOptions
        Dim payerId As String = m.Groups(1).Value.Trim()
        Dim payerName As String = System.Net.WebUtility.HtmlDecode(m.Groups(2).Value).Trim()

        If payerId <> "00000000-0000-0000-0000-000000000000" AndAlso
           Not String.IsNullOrWhiteSpace(payerName) AndAlso
           Not payers.ContainsKey(payerName) Then

            payers.Add(payerName, payerId)
        End If
    Next

    Console.WriteLine("Payers loaded: " & payers.Count.ToString())

Catch ex As Exception
    errorMessage = ex.ToString()
End Try