Try

    errorMessage = Nothing
    Dim managedGridResponse As String = Nothing
    dt = New System.Data.DataTable()

    Dim baseUrl As String = "https://homecare1.axxessweb.com"

    Dim url As String = baseUrl & "/Billing/ManagedGrid"

    Dim requestJson As String =
        Newtonsoft.Json.JsonConvert.SerializeObject(
            New With {
                .insuranceId = insuranceId,
                .branchId = branchId,
                .isFilterByAll = False,
                .patientTags = New String() {},
                .claimType = claimType,
                .sortBy = "DateRange",
                .isAscending = False,
                .pageNum = 1,
                .pageSize = 25
            })

    Dim request = CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)

    request.Method = "POST"
    request.CookieContainer = cookies
    request.Accept = "application/json, text/plain, */*"
    request.ContentType = "application/json;charset=UTF-8"
    request.UserAgent = "Mozilla/5.0"
    request.Referer = baseUrl & "/"
    request.Headers.Add("Origin", baseUrl)
    request.Headers.Add("x-build-date-identifier", buildDateIdentifier)
    request.Headers.Add("x-culturecode", "en-US")
    request.Headers.Add("Cache-Control", "no-cache")
    request.Headers.Add("Pragma", "no-cache")

    Dim bytes = System.Text.Encoding.UTF8.GetBytes(requestJson)

    request.ContentLength = bytes.Length

    Using stream = request.GetRequestStream()
        stream.Write(bytes, 0, bytes.Length)
    End Using

    Using response = CType(request.GetResponse(), System.Net.HttpWebResponse)
        Using reader As New System.IO.StreamReader(response.GetResponseStream())
            managedGridResponse = reader.ReadToEnd()
        End Using
    End Using

    If String.IsNullOrWhiteSpace(managedGridResponse) Then
        Throw New Exception("ManagedGrid response is empty.")
    End If

    If managedGridResponse.TrimStart().StartsWith("<") Then
        Throw New Exception("Expected JSON but received HTML.")
    End If

    Dim json = Newtonsoft.Json.Linq.JObject.Parse(managedGridResponse)

    Dim items = CType(json("data")("items"), Newtonsoft.Json.Linq.JArray)

    dt.Columns.Add("Id")
    dt.Columns.Add("PatientId")
    dt.Columns.Add("DisplayName")
    dt.Columns.Add("FirstName")
    dt.Columns.Add("LastName")
    dt.Columns.Add("PatientIdNumber")
    dt.Columns.Add("AgencyLocationId")
    dt.Columns.Add("PrimaryInsuranceId")
    dt.Columns.Add("StartDate", GetType(DateTime))
    dt.Columns.Add("EndDate", GetType(DateTime))
    dt.Columns.Add("DateRange")
    dt.Columns.Add("Status", GetType(Integer))
    dt.Columns.Add("StatusName")
    dt.Columns.Add("FacilityType", GetType(Integer))
    dt.Columns.Add("BillType", GetType(Integer))
    dt.Columns.Add("IsVerified", GetType(Boolean))
    dt.Columns.Add("IsInfoVerified", GetType(Boolean))
    dt.Columns.Add("IsVisitVerified", GetType(Boolean))
    dt.Columns.Add("IsSupplyVerified", GetType(Boolean))
    dt.Columns.Add("IsDocumentationRequired", GetType(Boolean))
    dt.Columns.Add("PatientStatus", GetType(Integer))
    dt.Columns.Add("AuthorizationStatus")

    For Each item As Newtonsoft.Json.Linq.JObject In items

        Dim row = dt.NewRow()

        row("Id") = item("id").ToString()
        row("PatientId") = item("patientId").ToString()
        row("DisplayName") = item("displayName").ToString()
        row("FirstName") = item("firstName").ToString()
        row("LastName") = item("lastName").ToString()
        row("PatientIdNumber") = item("patientIdNumber").ToString()
        row("AgencyLocationId") = item("agencyLocationId").ToString()
        row("PrimaryInsuranceId") = item("primaryInsuranceId").ToString()
        row("StartDate") = DateTime.Parse(item("startDate").ToString())
        row("EndDate") = DateTime.Parse(item("endDate").ToString())
        row("DateRange") = item("dateRange").ToString()
        row("Status") = CInt(item("status"))
        row("StatusName") = item("statusName").ToString()
        row("FacilityType") = CInt(item("facilityType"))
        row("BillType") = CInt(item("billType"))
        row("IsVerified") = CBool(item("isVerified"))
        row("IsInfoVerified") = CBool(item("isInfoVerified"))
        row("IsVisitVerified") = CBool(item("isVisitVerified"))
        row("IsSupplyVerified") = CBool(item("isSupplyVerified"))
        row("IsDocumentationRequired") = CBool(item("isDocumentationRequired"))
        row("PatientStatus") = CInt(item("status"))
        row("AuthorizationStatus") = item("authorizationStatus").ToString()

        dt.Rows.Add(row)

    Next

   
	If dt IsNot Nothing AndAlso dt.Rows.Count > 0 Then
	Console.WriteLine("Managed claims found: " & dt.Rows.Count)
  	Dim rows = dt.AsEnumerable().Where(Function(r) r.Field(Of DateTime)("StartDate").ToString("M/d/yyyy") = startDate AndAlso r.Field(Of DateTime)("EndDate").ToString("M/d/yyyy") = endDate)

    dt = If(rows.Any(), rows.CopyToDataTable(), dt.Clone())

	End If

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