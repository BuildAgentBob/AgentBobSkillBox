Try
    errorMessage = Nothing
    verificationStatus = ""
    isVerified = False

    infoVerifyResponse = Nothing
    insuranceVerifyResponse = Nothing
    visitVerifyResponse = Nothing
    supplyVerifyResponse = Nothing
    completeResponse = Nothing

    Dim baseUrl As String = "https://homecare1.axxessweb.com"
    If String.IsNullOrWhiteSpace(serviceId) Then serviceId = "2"

    ' =====================================================
    ' SMALL HELPERS
    ' =====================================================
	Dim cookieJar As System.Net.CookieContainer = cookies
    Dim postForm =
        Function(endpoint As String, formBody As String, accept As String) As String

            Dim req = CType(System.Net.WebRequest.Create(baseUrl & endpoint), System.Net.HttpWebRequest)
			
            req.Method = "POST"
            req.CookieContainer = cookieJar
            req.Accept = accept
            req.ContentType = "application/x-www-form-urlencoded; charset=UTF-8"
            req.UserAgent = "Mozilla/5.0"
            req.Referer = baseUrl & "/"
            req.Headers.Add("Origin", baseUrl)
            req.Headers.Add("x-requested-with", "XMLHttpRequest")
            req.Headers.Add("x-build-date-identifier", buildDateIdentifier)
            req.Headers.Add("x-culturecode", "en-US")
            req.Headers.Add("Cache-Control", "no-cache")
            req.Headers.Add("Pragma", "no-cache")

            Dim bytes = System.Text.Encoding.UTF8.GetBytes(formBody)
            req.ContentLength = bytes.Length

            Using s = req.GetRequestStream()
                s.Write(bytes, 0, bytes.Length)
            End Using

            Using resp = CType(req.GetResponse(), System.Net.HttpWebResponse)
                Using reader As New System.IO.StreamReader(resp.GetResponseStream())
                    Return reader.ReadToEnd()
                End Using
            End Using

        End Function
		

    Dim postJson =
        Function(endpoint As String, jsonBody As String) As String

            Dim req = CType(System.Net.WebRequest.Create(baseUrl & endpoint), System.Net.HttpWebRequest)
            req.Method = "POST"
            req.CookieContainer = cookieJar
            req.Accept = "application/json, text/javascript, */*; q=0.01"
            req.ContentType = "application/json"
            req.UserAgent = "Mozilla/5.0"
            req.Referer = baseUrl & "/"
            req.Headers.Add("Origin", baseUrl)
            req.Headers.Add("x-requested-with", "XMLHttpRequest")
            req.Headers.Add("x-build-date-identifier", buildDateIdentifier)
            req.Headers.Add("x-culturecode", "en-US")
            req.Headers.Add("Cache-Control", "no-cache")
            req.Headers.Add("Pragma", "no-cache")

            Dim bytes = System.Text.Encoding.UTF8.GetBytes(jsonBody)
            req.ContentLength = bytes.Length

            Using s = req.GetRequestStream()
                s.Write(bytes, 0, bytes.Length)
            End Using

            Using resp = CType(req.GetResponse(), System.Net.HttpWebResponse)
                Using reader As New System.IO.StreamReader(resp.GetResponseStream())
                    Return reader.ReadToEnd()
                End Using
            End Using

        End Function

    Dim assertSuccess =
        Sub(responseText As String, stepName As String)

            If String.IsNullOrWhiteSpace(responseText) Then
                Throw New Exception(stepName & " response is empty.")
            End If

            If responseText.TrimStart().StartsWith("<") Then
                Throw New Exception(stepName & " expected JSON but received HTML. Response starts with: " &
                                    responseText.Substring(0, Math.Min(300, responseText.Length)))
            End If

            Dim j = Newtonsoft.Json.Linq.JObject.Parse(responseText)

            If j("isSuccessful") Is Nothing OrElse Not CBool(j("isSuccessful")) Then
                Dim msg As String = If(j("errorMessage") Is Nothing, responseText, j("errorMessage").ToString())
                Throw New Exception(stepName & " failed: " & msg)
            End If

            Console.WriteLine(stepName & " successful: " & j("errorMessage").ToString())

        End Sub

    Dim serializeForm =
        Function(html As String, formId As String) As String

            Dim formMatch = System.Text.RegularExpressions.Regex.Match(
                html,
                "<form[^>]*id=[""']" & System.Text.RegularExpressions.Regex.Escape(formId) & "[""'][\s\S]*?</form>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)

            If Not formMatch.Success Then
                Throw New Exception("Form not found: " & formId)
            End If

            Dim formHtml As String = formMatch.Value
            Dim parts As New List(Of String)

            ' Inputs
            For Each m As System.Text.RegularExpressions.Match In System.Text.RegularExpressions.Regex.Matches(
                formHtml,
                "<input\b[^>]*>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                Dim tag As String = m.Value

                Dim nameMatch = System.Text.RegularExpressions.Regex.Match(tag, "\bname=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                If Not nameMatch.Success Then Continue For

                Dim typeMatch = System.Text.RegularExpressions.Regex.Match(tag, "\btype=[""']([^""']+)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                Dim valueMatch = System.Text.RegularExpressions.Regex.Match(tag, "\bvalue=[""']([^""']*)[""']", System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                Dim inputType As String = If(typeMatch.Success, typeMatch.Groups(1).Value.ToLower(), "text")
                Dim inputName As String = System.Net.WebUtility.HtmlDecode(nameMatch.Groups(1).Value)
                Dim inputValue As String = If(valueMatch.Success, System.Net.WebUtility.HtmlDecode(valueMatch.Groups(1).Value), "")

                If inputType = "checkbox" OrElse inputType = "radio" Then
                    If Not tag.ToLower().Contains("checked") Then Continue For
                    If String.IsNullOrWhiteSpace(inputValue) Then inputValue = "true"
                End If

                parts.Add(Uri.EscapeDataString(inputName) & "=" & Uri.EscapeDataString(inputValue))

            Next

            ' Textareas
            For Each m As System.Text.RegularExpressions.Match In System.Text.RegularExpressions.Regex.Matches(
                formHtml,
                "<textarea\b[^>]*name=[""']([^""']+)[""'][^>]*>([\s\S]*?)</textarea>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                Dim n As String = System.Net.WebUtility.HtmlDecode(m.Groups(1).Value)
                Dim v As String = System.Net.WebUtility.HtmlDecode(m.Groups(2).Value).Trim()
                parts.Add(Uri.EscapeDataString(n) & "=" & Uri.EscapeDataString(v))

            Next

            ' Selects
            For Each m As System.Text.RegularExpressions.Match In System.Text.RegularExpressions.Regex.Matches(
                formHtml,
                "<select\b[^>]*name=[""']([^""']+)[""'][^>]*>([\s\S]*?)</select>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                Dim n As String = System.Net.WebUtility.HtmlDecode(m.Groups(1).Value)
                Dim selectHtml As String = m.Groups(2).Value
                Dim selectedValue As String = ""

                Dim selectedOption = System.Text.RegularExpressions.Regex.Match(
                    selectHtml,
                    "<option\b[^>]*selected[^>]*value=[""']([^""']*)[""'][^>]*>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)

                If Not selectedOption.Success Then
                    selectedOption = System.Text.RegularExpressions.Regex.Match(
                        selectHtml,
                        "<option\b[^>]*value=[""']([^""']*)[""'][^>]*>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                End If

                If selectedOption.Success Then
                    selectedValue = System.Net.WebUtility.HtmlDecode(selectedOption.Groups(1).Value)
                End If

                parts.Add(Uri.EscapeDataString(n) & "=" & Uri.EscapeDataString(selectedValue))

            Next

            Return String.Join("&", parts)

        End Function

    ' =====================================================
    ' 1. LOAD MANAGED CLAIM PAGE
    ' =====================================================

    Console.WriteLine("Loading managed claim...")

    Dim loadBody As String =
        "Id=" & Uri.EscapeDataString(claimId) &
        "&PatientId=" & Uri.EscapeDataString(patientId)

    Dim managedClaimHtml As String =
        postForm("/Billing/ManagedClaim", loadBody, "text/html, */*; q=0.01")

    If Not managedClaimHtml.Contains("managedBillingInfo") Then
        Throw New Exception("ManagedClaim page did not contain managedBillingInfo form.")
    End If

    verificationStatus &= "ManagedClaim loaded. "

    ' =====================================================
    ' 2. MANAGED INFO VERIFY
    ' =====================================================

    Console.WriteLine("Verifying managed claim info...")

    Dim infoFormBody As String = serializeForm(managedClaimHtml, "managedBillingInfo")

    infoVerifyResponse =
        postForm("/Billing/ManagedInfoVerify", infoFormBody, "application/json, text/javascript, */*; q=0.01")

    assertSuccess(infoVerifyResponse, "ManagedInfoVerify")
    verificationStatus &= "Info verified. "

    ' =====================================================
    ' 3. LOAD INSURANCE PAGE
    ' =====================================================

    Console.WriteLine("Loading managed claim insurance...")

    Dim tabBody As String =
        "Id=" & Uri.EscapeDataString(claimId) &
        "&PatientId=" & Uri.EscapeDataString(patientId) &
        "&ServiceId=" & Uri.EscapeDataString(serviceId) &
        "&Type=ManagedCare"

    Dim insuranceHtml As String =
        postForm("/Billing/ManagedClaimInsurance", tabBody, "*/*")

    If Not insuranceHtml.Contains("managedBillingInsuranceForm") Then
        Throw New Exception("ManagedClaimInsurance response did not contain managedBillingInsuranceForm.")
    End If

    ' Extract the JSON object containing the insurance model.
    ' We use the first object around InsuranceBillData/VisitLineItems from the returned page/model.
	Dim insuranceMarker As String = """MiddleInitial"""
	Dim markerIndex As Integer = insuranceHtml.IndexOf(insuranceMarker, StringComparison.OrdinalIgnoreCase)
	
	If markerIndex < 0 Then
	    insuranceMarker = """HasMultipleEpisodes"""
	    markerIndex = insuranceHtml.IndexOf(insuranceMarker, StringComparison.OrdinalIgnoreCase)
	End If
	
	If markerIndex < 0 Then
	    Throw New Exception("Could not find full insurance claim model in ManagedClaimInsurance response.")
	End If
	
	Dim startIndex As Integer = insuranceHtml.LastIndexOf("{"c, markerIndex)
	If startIndex < 0 Then
	    Throw New Exception("Could not locate start of insurance claim JSON object.")
	End If

    Dim depth As Integer = 0
    Dim inString As Boolean = False
    Dim escapeNext As Boolean = False
    Dim endIndex As Integer = -1

    For i As Integer = startIndex To insuranceHtml.Length - 1
        Dim ch As Char = insuranceHtml(i)

        If escapeNext Then
            escapeNext = False
        ElseIf ch = "\"c AndAlso inString Then
            escapeNext = True
        ElseIf ch = """"c Then
            inString = Not inString
        ElseIf Not inString Then
            If ch = "{"c Then
                depth += 1
            ElseIf ch = "}"c Then
                depth -= 1
                If depth = 0 Then
                    endIndex = i
                    Exit For
                End If
            End If
        End If
    Next

    If endIndex < 0 Then
        Throw New Exception("Could not locate end of insurance JSON object.")
    End If

    Dim insuranceJsonBody As String = insuranceHtml.Substring(startIndex, endIndex - startIndex + 1)

    Dim insuranceObj As Newtonsoft.Json.Linq.JObject = Newtonsoft.Json.Linq.JObject.Parse(insuranceJsonBody)

    ' =====================================================
    ' 4. MANAGED INSURANCE VERIFY
    ' =====================================================

    Console.WriteLine("Verifying managed claim insurance...")
	Console.WriteLine("Insurance JSON starts:")
Console.WriteLine(insuranceJsonBody.Substring(0, Math.Min(1000, insuranceJsonBody.Length)))
    insuranceVerifyResponse =
        postJson("/Billing/ManagedInsuranceVerify", insuranceObj.ToString(Newtonsoft.Json.Formatting.None))

    assertSuccess(insuranceVerifyResponse, "ManagedInsuranceVerify")
    verificationStatus &= "Insurance verified. "

    ' =====================================================
    ' 5. LOAD VISIT PAGE
    ' =====================================================

    Console.WriteLine("Loading managed claim visits...")

    Dim visitHtml As String =
        postForm("/Billing/ManagedClaimVisit", tabBody, "*/*")

    If Not visitHtml.Contains("managedBillingVisitForm") Then
        Throw New Exception("ManagedClaimVisit response did not contain managedBillingVisitForm.")
    End If

    ' =====================================================
    ' 6. MANAGED VISIT VERIFY
    ' =====================================================

    Console.WriteLine("Verifying managed claim visits...")

    Dim visitFormBody As String = serializeForm(visitHtml, "managedBillingVisitForm")

    visitVerifyResponse =
        postForm("/Billing/ManagedVisitVerify", visitFormBody, "application/json, text/javascript, */*; q=0.01")

    assertSuccess(visitVerifyResponse, "ManagedVisitVerify")
    verificationStatus &= "Visits verified. "

    ' =====================================================
    ' 7. MANAGED SUPPLY VERIFY
    ' =====================================================

    Console.WriteLine("Verifying managed claim supplies...")

    Dim supplyBody As String =
        "Id=" & Uri.EscapeDataString(claimId) &
        "&PatientId=" & Uri.EscapeDataString(patientId) &
        "&Type=ManagedCare"

    supplyVerifyResponse =
        postForm("/Billing/ManagedSupplyVerify", supplyBody, "application/json, text/javascript, */*; q=0.01")

    assertSuccess(supplyVerifyResponse, "ManagedSupplyVerify")
    verificationStatus &= "Supply verified. "

   ' =====================================================
' 8. LOAD SUMMARY + COMPLETE MANAGED CLAIM
' =====================================================

		Console.WriteLine("Loading managed claim summary...")
		
		Dim summaryHtml As String =
		    postForm("/Billing/ManagedClaimSummary", tabBody, "*/*")
		
		If String.IsNullOrWhiteSpace(summaryHtml) Then
		    Throw New Exception("ManagedClaimSummary response is empty.")
		End If
		
		If Not summaryHtml.Contains("managedCompleteForm") Then
		    Throw New Exception("ManagedClaimSummary response did not contain managedCompleteForm.")
		End If
		
		' Build the form exactly as the browser does
		Dim completeBody As String = serializeForm(summaryHtml, "managedCompleteForm")
		
		If String.IsNullOrWhiteSpace(completeBody) Then
		    Throw New Exception("ManagedComplete form could not be serialized.")
		End If

' Validate required values exist
		Dim totalMatch As System.Text.RegularExpressions.Match =
		    System.Text.RegularExpressions.Regex.Match(
		        completeBody,
		        "(?:^|&)Total=([^&]*)",
		        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
		
		If Not totalMatch.Success Then
		    Throw New Exception("ManagedClaimSummary did not contain Total.")
		End If
		
		Dim total As String = Uri.UnescapeDataString(totalMatch.Groups(1).Value)
		
		If String.IsNullOrWhiteSpace(total) Then
		    Throw New Exception("ManagedClaimSummary returned an empty Total.")
		End If
		
		Dim expectedMatch As System.Text.RegularExpressions.Match =
		    System.Text.RegularExpressions.Regex.Match(
		        completeBody,
		        "(?:^|&)ExpectedTotal=([^&]*)",
		        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
		
		If Not expectedMatch.Success Then
		    Throw New Exception("ManagedClaimSummary did not contain ExpectedTotal.")
		End If
		
		Dim expectedTotal As String = Uri.UnescapeDataString(expectedMatch.Groups(1).Value)
		
		If String.IsNullOrWhiteSpace(expectedTotal) Then
		    Throw New Exception("ManagedClaimSummary returned an empty ExpectedTotal.")
		End If
		
		Dim totalValue As Decimal
		
		If Not Decimal.TryParse(total, System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture, totalValue) Then
		    Throw New Exception("ManagedClaimSummary returned an invalid Total: " & total)
		End If
		
		If totalValue <= 0D Then
		    Throw New Exception("ManagedClaimSummary returned a Total of 0. Claim cannot be completed.")
		End If

Console.WriteLine("ManagedComplete Total = " & total)
Console.WriteLine("ManagedComplete ExpectedTotal = " & expectedTotal)

Console.WriteLine("Completing managed claim...")

completeResponse =
    postForm(
        "/Billing/ManagedComplete",
        completeBody,
        "application/json, text/javascript, */*; q=0.01")

assertSuccess(completeResponse, "ManagedComplete")

verificationStatus &= "Claim completed. "

isVerified = True

Catch ex As System.Net.WebException

    If ex.Response IsNot Nothing Then
        Using response = CType(ex.Response, System.Net.HttpWebResponse)
            Using reader As New System.IO.StreamReader(response.GetResponseStream())
                errorMessage = response.StatusCode.ToString() & ": " & reader.ReadToEnd()
            End Using
        End Using
    Else
        errorMessage = ex.Message
    End If

Catch ex As Exception
    errorMessage = ex.Message
End Try