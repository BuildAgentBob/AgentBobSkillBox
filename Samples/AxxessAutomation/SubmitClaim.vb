Try

    errorMessage = Nothing
    submitClaimResponse = Nothing

    Dim baseUrl As String = "https://homecare1.axxessweb.com"

    Dim payload As New Newtonsoft.Json.Linq.JObject(
        New Newtonsoft.Json.Linq.JProperty("BranchId", branchId),
        New Newtonsoft.Json.Linq.JProperty("InsuranceId", insuranceId),
        New Newtonsoft.Json.Linq.JProperty("PatientTags", New Newtonsoft.Json.Linq.JArray()),
        New Newtonsoft.Json.Linq.JProperty("IsFilterByAll", False),
        New Newtonsoft.Json.Linq.JProperty("ClaimType", 3),
        New Newtonsoft.Json.Linq.JProperty("claimSelected",
            New Newtonsoft.Json.Linq.JArray(claimId))
    )

    Dim jsonBody As String = payload.ToString(Newtonsoft.Json.Formatting.None)

    Dim request =
        CType(System.Net.WebRequest.Create(baseUrl & "/Billing/SubmitClaimDirectly"),
              System.Net.HttpWebRequest)

    request.Method = "POST"
    request.CookieContainer = cookies
    request.Accept = "application/json, text/plain, */*"
    request.ContentType = "application/json;charset=UTF-8"
    request.UserAgent = "Mozilla/5.0"
    request.Referer = baseUrl & "/"

    request.Headers.Add("Origin", baseUrl)
    request.Headers.Add("x-build-date-identifier", buildDateIdentifier)
    request.Headers.Add("x-culturecode", "en-US")
    request.Headers.Add("x-requested-with", "XMLHttpRequest")
    request.Headers.Add("Cache-Control", "no-cache")
    request.Headers.Add("Pragma", "no-cache")

    Dim bytes = System.Text.Encoding.UTF8.GetBytes(jsonBody)
    request.ContentLength = bytes.Length

    Using stream = request.GetRequestStream()
        stream.Write(bytes, 0, bytes.Length)
    End Using

    Using response =
        CType(request.GetResponse(), System.Net.HttpWebResponse)

        Using reader As New System.IO.StreamReader(response.GetResponseStream())
            submitClaimResponse = reader.ReadToEnd()
        End Using

    End Using

    Dim json = Newtonsoft.Json.Linq.JObject.Parse(submitClaimResponse)

    If Not CBool(json("isSuccessful")) Then
        Throw New Exception(json("errorMessage").ToString())
    End If

    Console.WriteLine(json("errorMessage").ToString())

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