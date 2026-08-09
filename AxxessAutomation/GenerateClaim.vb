Try
    errorMessage = Nothing
    Dim managedCreateGridResponse As String = Nothing
    dt = New System.Data.DataTable()

    Dim baseUrl As String = "https://homecare1.axxessweb.com"

    Dim url As String =
        baseUrl & "/Billing/ManagedCreateGrid" &
        "?BranchId=" & Uri.EscapeDataString(branchId) &
        "&InsuranceId=" & Uri.EscapeDataString(insuranceId) &
        "&StartDate=" & Uri.EscapeDataString(startDate) &
        "&EndDate=" & Uri.EscapeDataString(endDate) &
        "&AuthorizationStatus=" &
        "&PatientTags=" &
        "&IsFilterTagsByAll=false" &
        "&IgnoreAutoRebind=false"

    Dim formBody As String =
        "BranchId=" & Uri.EscapeDataString(branchId) &
        "&InsuranceId=" & Uri.EscapeDataString(insuranceId) &
        "&StartDate=" & Uri.EscapeDataString(startDate) &
        "&EndDate=" & Uri.EscapeDataString(endDate) &
        "&AuthorizationStatus=" &
        "&PatientTags=" &
        "&IsFilterTagsByAll=false"

    Dim request = CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)
    request.Method = "POST"
    request.CookieContainer = cookies
    request.Accept = "*/*"
    request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8"
    request.UserAgent = "Mozilla/5.0"
    request.Referer = baseUrl & "/"
    request.Headers.Add("Origin", baseUrl)
    request.Headers.Add("x-requested-with", "XMLHttpRequest")
    request.Headers.Add("x-build-date-identifier", buildDateIdentifier)
    request.Headers.Add("x-culturecode", "en-US")
    request.Headers.Add("Cache-Control", "no-cache")
    request.Headers.Add("Pragma", "no-cache")

    Dim bodyBytes = System.Text.Encoding.UTF8.GetBytes(formBody)
    request.ContentLength = bodyBytes.Length

    Using stream = request.GetRequestStream()
        stream.Write(bodyBytes, 0, bodyBytes.Length)
    End Using

    Using response = CType(request.GetResponse(), System.Net.HttpWebResponse)
        Using reader As New System.IO.StreamReader(response.GetResponseStream())
            managedCreateGridResponse = reader.ReadToEnd()
        End Using
    End Using

    If String.IsNullOrWhiteSpace(managedCreateGridResponse) Then
        Throw New Exception("ManagedCreateGrid response is empty.")
    End If

    If managedCreateGridResponse.TrimStart().StartsWith("<") Then
        Throw New Exception("Expected JSON but received HTML. Response starts with: " &
                            managedCreateGridResponse.Substring(0, Math.Min(300, managedCreateGridResponse.Length)))
    End If

    Dim json = Newtonsoft.Json.Linq.JObject.Parse(managedCreateGridResponse)
    Dim dataArray = CType(json("Data"), Newtonsoft.Json.Linq.JArray)

    dt.Columns.Add("Id", GetType(String))
    dt.Columns.Add("PatientId", GetType(String))
    dt.Columns.Add("PayorId", GetType(String))
    dt.Columns.Add("FirstName", GetType(String))
    dt.Columns.Add("LastName", GetType(String))
    dt.Columns.Add("PayorName", GetType(String))
    dt.Columns.Add("ClaimTypes", GetType(Int32))
    dt.Columns.Add("PatientIdNumber", GetType(String))
    dt.Columns.Add("IsOverlappingClaimExist", GetType(Boolean))
    dt.Columns.Add("IsClaimCreate", GetType(Boolean))
    dt.Columns.Add("StartDate", GetType(DateTime))
    dt.Columns.Add("EndDate", GetType(DateTime))
    dt.Columns.Add("AuthorizationStatus", GetType(String))
    dt.Columns.Add("AuthorizationId", GetType(String))
    dt.Columns.Add("Tags", GetType(String))
    dt.Columns.Add("UserId", GetType(String))
    dt.Columns.Add("AgencyLocationId", GetType(String))
    dt.Columns.Add("DisplayName", GetType(String))

    For Each item As Newtonsoft.Json.Linq.JObject In dataArray
        Dim row = dt.NewRow()

        row("Id") = item("Id").ToString()
        row("PatientId") = item("PatientId").ToString()
        row("PayorId") = item("PayorId").ToString()
        row("FirstName") = item("FirstName").ToString()
        row("LastName") = item("LastName").ToString()
        row("PayorName") = item("PayorName").ToString()
        row("ClaimTypes") = CInt(item("ClaimTypes"))
        row("PatientIdNumber") = item("PatientIdNumber").ToString()
        row("IsOverlappingClaimExist") = CBool(item("IsOverlappingClaimExist"))
        row("IsClaimCreate") = CBool(item("IsClaimCreate"))
        row("StartDate") = DateTime.Parse(item("StartDate").ToString())
        row("EndDate") = DateTime.Parse(item("EndDate").ToString())
        row("AuthorizationStatus") = item("AuthorizationStatus").ToString()
        row("AuthorizationId") = item("AuthorizationId").ToString()
        row("Tags") = item("Tags").ToString()
        row("UserId") = item("UserId").ToString()
        row("AgencyLocationId") = item("AgencyLocationId").ToString()
        row("DisplayName") = item("DisplayName").ToString()

        dt.Rows.Add(row)
    Next
	
If shouldExcludeOverlappingClaim AndAlso (dt IsNot Nothing AndAlso dt.Rows.Count > 0) Then

    Dim rows = dt.AsEnumerable().Where(Function(r) Not CBool(r("IsOverlappingClaimExist")))

    dt = If(rows.Any(),rows.CopyToDataTable(),dt.Clone())
End If

Catch ex As System.Net.WebException
    If ex.Response IsNot Nothing Then
        Using response = CType(ex.Response, System.Net.HttpWebResponse)
            Using reader As New System.IO.StreamReader(response.GetResponseStream())
                errorMessage = response.StatusCode.ToString() & ": " & reader.ReadToEnd()
            End Using
        End Using
    Else
        errorMessage = ex.ToString()
    End If

Catch ex As Exception
    errorMessage = ex.ToString()
End Try