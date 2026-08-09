Dim addClaimsResponse As String
Try

    errorMessage = ""
    dtCreatedClaims = New DataTable()

    Dim url As String = "https://homecare1.axxessweb.com/Billing/Managed/Claims/Add"

    '====================================================
    ' Build form data
    '====================================================
    Dim form As New System.Text.StraingBuilder()

    For i As Integer = 0 To dt.Rows.Count - 1

        Dim r As DataRow = dt.Rows(i)

        If form.Length > 0 Then form.Append("&")

        form.Append("claims[" & i & "][PrimaryInsuranceId]=" & Uri.EscapeDataString(r("PayorId").ToString()))
        form.Append("&claims[" & i & "][PatientId]=" & Uri.EscapeDataString(r("PatientId").ToString()))
        form.Append("&claims[" & i & "][StartDate]=" & Uri.EscapeDataString(DateTime.Parse(r("StartDate").ToString()).ToString("yyyy-MM-ddTHH:mm:ss")))
        form.Append("&claims[" & i & "][EndDate]=" & Uri.EscapeDataString(DateTime.Parse(r("EndDate").ToString()).ToString("yyyy-MM-ddTHH:mm:ss")))
        form.Append("&claims[" & i & "][InvoiceType]=" & Uri.EscapeDataString(r("ClaimTypes").ToString()))
        form.Append("&claims[" & i & "][UserId]=" & Uri.EscapeDataString(r("UserId").ToString()))
        form.Append("&claims[" & i & "][Authorization]=" & Uri.EscapeDataString(r("AuthorizationId").ToString()))
        form.Append("&claims[" & i & "][AgencyLocationId]=" & Uri.EscapeDataString(r("AgencyLocationId").ToString()))

    Next

    Dim bodyBytes() As Byte = System.Text.Encoding.UTF8.GetBytes(form.ToString())

    '====================================================
    ' Request
    '====================================================
    Dim request =
        CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)

    request.Method = "POST"
    request.CookieContainer = cookies
    request.Accept = "application/json, text/javascript, */*; q=0.01"
    request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8"
    request.UserAgent = "Mozilla/5.0"
    request.Referer = "https://homecare1.axxessweb.com/"
    request.Headers.Add("Origin", "https://homecare1.axxessweb.com")
    request.Headers.Add("x-requested-with", "XMLHttpRequest")
    request.Headers.Add("x-build-date-identifier", buildDateIdentifier)
    request.Headers.Add("x-culturecode", "en-US")
    request.Headers.Add("Cache-Control", "no-cache")
    request.Headers.Add("Pragma", "no-cache")
    request.ContentLength = bodyBytes.Length

    Using stream = request.GetRequestStream()
        stream.Write(bodyBytes, 0, bodyBytes.Length)
    End Using

    '====================================================
    ' Response
    '====================================================
    Using response =
        CType(request.GetResponse(), System.Net.HttpWebResponse)

        Using reader As New System.IO.StreamReader(response.GetResponseStream())
            addClaimsResponse = reader.ReadToEnd()
        End Using

    End Using

    'Console.WriteLine(addClaimsResponse)

    '====================================================
    ' Convert returned Data array to DataTable
    '====================================================
    Dim json = Newtonsoft.Json.Linq.JObject.Parse(addClaimsResponse)

    Dim dataArray =
        CType(json("Data"), Newtonsoft.Json.Linq.JArray)

    dtCreatedClaims =
        Newtonsoft.Json.JsonConvert.DeserializeObject(Of DataTable)(dataArray.ToString())

    Console.WriteLine("Created Claims: " & dtCreatedClaims.Rows.Count)

Catch ex As Exception
	
	If Not String.IsNullOrWhiteSpace(addClaimsResponse) Then

		    Try
		
		        Dim jsonText As String = addClaimsResponse.Trim()
		
		        ' If the response is an escaped JSON string, unescape it first.
		        If jsonText.StartsWith("""{") Then
		            jsonText = Newtonsoft.Json.JsonConvert.DeserializeObject(Of String)(jsonText)
		        End If
		
		        Dim j = Newtonsoft.Json.Linq.JObject.Parse(jsonText)
		
		        errorMessage = If(j("errorMessage"), ex.ToString()).ToString()
		
		    Catch
		        ' Response was not JSON. Ignore.
				    errorMessage = ex.ToString()
		    End Try
	Else
	    errorMessage = ex.ToString()
	End If

End Try