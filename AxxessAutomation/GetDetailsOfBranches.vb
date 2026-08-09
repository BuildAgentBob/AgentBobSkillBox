Try
    errorMessage = Nothing
    commonResponse = Nothing
    branches = New Dictionary(Of String, Object)

    Dim url As String = "https://homecare1.axxessweb.com/Common"

    Dim request = CType(System.Net.WebRequest.Create(url), System.Net.HttpWebRequest)
    request.Method = "GET"
    request.CookieContainer = cookies
    request.Accept = "application/json, text/plain, */*"
    request.UserAgent = "Mozilla/5.0"
    request.Referer = "https://homecare1.axxessweb.com/"
    request.Headers.Add("x-build-date-identifier", buildDateIdentifier)
    request.Headers.Add("x-culturecode", "en-US")
    request.Headers.Add("Cache-Control", "no-cache")
    request.Headers.Add("Pragma", "no-cache")

    Using response = CType(request.GetResponse(), System.Net.HttpWebResponse)
        Using reader As New System.IO.StreamReader(response.GetResponseStream())
            commonResponse = reader.ReadToEnd()
        End Using
    End Using

    If commonResponse.TrimStart().StartsWith("<") Then
        Throw New Exception("Expected JSON but received HTML. HomeCare session is not established.")
    End If

    Dim json = Newtonsoft.Json.Linq.JObject.Parse(commonResponse)

    branches =
        json("branches").
        ToObject(Of Newtonsoft.Json.Linq.JArray)().
        ToDictionary(
            Function(b) b("name").ToString().Trim(),
            Function(b) CType(b("id").ToString(), Object)
        )

    Console.WriteLine("Branches loaded: " & branches.Count.ToString())

Catch ex As Exception
    errorMessage = ex.ToString()
End Try