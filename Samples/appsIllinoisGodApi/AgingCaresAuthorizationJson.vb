' Builds client-authorization schema_version 2.0 JSON (snake_case) from archived AgingCares HTML.
' Requires ViewPlanOfCare in htmlPaths; uses ViewProviderParticipantHub when present.
' Other hub section HTMLs supplement fields. Unknown scalars null; empty lists [].
'
' In:  htmlPaths (String())
'       pdfFilePath (String) — full local PDF path; leaf name → source.key
'       pageCount (Int32) — source.page_count; 0 → null in JSON
' Out: outputJson, errorMessage

Dim success As Boolean = False

Try
    errorMessage = ""
    outputJson = ""

    Console.WriteLine("=== AgingCares Authorization JSON (v2) ===")

    If htmlPaths Is Nothing OrElse htmlPaths.Length = 0 Then
        Throw New System.Exception("htmlPaths is required (array of local HTML file paths).")
    End If

    Dim pdfPathIn As String = If(pdfFilePath, "").Trim()
    If String.IsNullOrWhiteSpace(pdfPathIn) Then
        Throw New System.Exception("pdfFilePath is required (full path to the authorization PDF).")
    End If

    Dim pdfPathForHash As String = System.IO.Path.GetFullPath(pdfPathIn)
    If Not System.IO.File.Exists(pdfPathForHash) Then
        Throw New System.Exception("PDF not found: " & pdfPathForHash)
    End If

    Dim pdfLeafName As String = System.IO.Path.GetFileName(pdfPathForHash)
    If String.IsNullOrWhiteSpace(pdfLeafName) OrElse
       pdfLeafName.IndexOf("..", StringComparison.Ordinal) >= 0 Then
        Throw New System.Exception("pdfFilePath must point to a valid PDF file.")
    End If

    Dim pdfBytes As Byte() = System.IO.File.ReadAllBytes(pdfPathForHash)

    Dim pdfHashHex As String
    Using sha As System.Security.Cryptography.SHA256 =
        System.Security.Cryptography.SHA256.Create()

        Dim hashBytes As Byte() = sha.ComputeHash(pdfBytes)
        pdfHashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()
    End Using

    Console.WriteLine(
        "PDF source | name=" & pdfLeafName &
        " | hashPath=" & pdfPathForHash &
        " | DOCUMENT_HASH chars=" & pdfHashHex.Length.ToString() &
        " | PAGE_COUNT(in)=" & pageCount.ToString()
    )

    ' Local defaults — edit here (not UiPath args)
    Dim outputJsonPath As String = Nothing
    Dim sourceBucket As String = "helpathome-us-dev-data-raw"
    Dim market As String = "IL"
    Dim processingDate As String = System.DateTime.UtcNow.ToString("yyyy-MM-dd")
    Dim category As String = "authorization"
    Dim pipelineVersion As String = Nothing
    Dim promptVersion As String = Nothing
    Dim modelId As String = "uipath-data-scrapping"

    Dim bucket As String = If(sourceBucket, "").Trim()
    Dim key As String =
        "pdf-extraction/raw/market=" & market &
        "/date=" & processingDate &
        "/category=" & category &
        "/" & pdfLeafName

    Dim mkt As String = If(String.IsNullOrWhiteSpace(market), "IL", market.Trim().ToUpperInvariant())
    Dim cat As String = If(String.IsNullOrWhiteSpace(category), "authorization", category.Trim())
    Dim procDate As String = If(processingDate, "").Trim()
    If String.IsNullOrWhiteSpace(procDate) Then
        procDate = System.DateTime.UtcNow.ToString("yyyy-MM-dd")
    End If

    Dim pipeVer As String = If(pipelineVersion, "").Trim()
    If String.IsNullOrWhiteSpace(pipeVer) Then pipeVer = Nothing
    Dim promptVer As String = If(promptVersion, "").Trim()
    If String.IsNullOrWhiteSpace(promptVer) Then promptVer = Nothing
    Dim model As String = If(modelId, "").Trim()
    If String.IsNullOrWhiteSpace(model) Then model = "uipath-data-scrapping"
    Dim docHash As String = pdfHashHex
    Dim pages As System.Nullable(Of Integer) = Nothing
    If pageCount > 0 Then pages = pageCount

    ' ---------- helpers ----------
    Dim HtmlDecode As Func(Of String, String) =
        Function(s As String) As String
            If String.IsNullOrEmpty(s) Then Return s
            Return System.Net.WebUtility.HtmlDecode(s)
        End Function

    Dim GetInputValue As Func(Of String, String, String) =
        Function(html As String, fieldId As String) As String
            If String.IsNullOrWhiteSpace(html) OrElse String.IsNullOrWhiteSpace(fieldId) Then
                Return Nothing
            End If
            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    html,
                    "id\s*=\s*""" & System.Text.RegularExpressions.Regex.Escape(fieldId) &
                    """[^>]*\bvalue\s*=\s*""([^""]*)""",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            If Not m.Success Then
                m = System.Text.RegularExpressions.Regex.Match(
                    html,
                    "\bvalue\s*=\s*""([^""]*)""[^>]*\bid\s*=\s*""" &
                    System.Text.RegularExpressions.Regex.Escape(fieldId) & """",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            End If
            If Not m.Success Then Return Nothing
            Dim v As String = HtmlDecode(m.Groups(1).Value).Trim()
            If v.Length = 0 OrElse
               String.Equals(v, "N/A", StringComparison.OrdinalIgnoreCase) Then
                Return Nothing
            End If
            Return v
        End Function

    Dim VisibleLines As Func(Of String, System.Collections.Generic.List(Of String)) =
        Function(html As String) As System.Collections.Generic.List(Of String)
            Dim list As New System.Collections.Generic.List(Of String)()
            If String.IsNullOrEmpty(html) Then Return list
            Dim t As String = System.Text.RegularExpressions.Regex.Replace(
                html, "<script[\s\S]*?</script>", " ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            t = System.Text.RegularExpressions.Regex.Replace(
                t, "<style[\s\S]*?</style>", " ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            t = System.Text.RegularExpressions.Regex.Replace(
                t, "<(br|/p|/div|/tr|/h\d|/li|/td|/th)[^>]*>", vbLf,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            t = System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", " ")
            t = HtmlDecode(t)
            For Each raw As String In t.Split(
                New Char() {ChrW(10), ChrW(13)},
                System.StringSplitOptions.RemoveEmptyEntries)
                Dim ln As String = System.Text.RegularExpressions.Regex.Replace(
                    raw, "\s+", " ").Trim()
                If ln.Length > 0 Then list.Add(ln)
            Next
            Return list
        End Function

    Dim LineAfterLabel As Func(Of System.Collections.Generic.List(Of String), String, String) =
        Function(lines As System.Collections.Generic.List(Of String), label As String) As String
            If lines Is Nothing Then Return Nothing
            For i As Integer = 0 To lines.Count - 2
                If String.Equals(lines(i), label, StringComparison.OrdinalIgnoreCase) Then
                    Dim nxt As String = lines(i + 1).Trim()
                    If nxt.Length = 0 OrElse
                       String.Equals(nxt, "N/A", StringComparison.OrdinalIgnoreCase) Then
                        Return Nothing
                    End If
                    Return nxt
                End If
            Next
            Return Nothing
        End Function

    Dim ParseDateYmd As Func(Of String, String) =
        Function(raw As String) As String
            If String.IsNullOrWhiteSpace(raw) Then Return Nothing
            Dim s As String = raw.Trim()
            ' strip trailing age e.g. "05/16/1965 (61)"
            s = System.Text.RegularExpressions.Regex.Replace(
                s, "\s*\(\d+\)\s*$", "").Trim()
            Dim mIso As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(s, "^\d{4}-\d{2}-\d{2}")
            If mIso.Success Then Return mIso.Value
            Dim dt As System.DateTime
            Dim formats() As String = {
                "M/d/yyyy h:mm:ss tt", "M/d/yyyy", "MM/dd/yyyy",
                "MMM d yyyy h:mmtt", "MMM d yyyy", "MMMM d yyyy",
                "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd"
            }
            If System.DateTime.TryParseExact(
                s, formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces, dt) Then
                Return dt.ToString("yyyy-MM-dd")
            End If
            If System.DateTime.TryParse(
                s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces, dt) Then
                Return dt.ToString("yyyy-MM-dd")
            End If
            Return Nothing
        End Function

    Dim ParseNumber As Func(Of String, Object) =
        Function(raw As String) As Object
            If String.IsNullOrWhiteSpace(raw) Then Return Nothing
            Dim s As String = raw.Trim()
            If String.Equals(s, "N", StringComparison.OrdinalIgnoreCase) Then Return Nothing
            Dim n As Decimal
            If Decimal.TryParse(
                s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, n) Then
                Return n
            End If
            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(s, "(\d+(?:\.\d+)?)")
            If m.Success AndAlso Decimal.TryParse(
                m.Groups(1).Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, n) Then
                Return n
            End If
            Return Nothing
        End Function

    Dim YnToBool As Func(Of String, Object) =
        Function(raw As String) As Object
            If String.IsNullOrWhiteSpace(raw) Then Return Nothing
            Dim s As String = raw.Trim()
            If String.Equals(s, "Y", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(s, "YES", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase) Then
                Return True
            End If
            If String.Equals(s, "N", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(s, "NO", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(s, "FALSE", StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If
            Return Nothing
        End Function

    Dim StateToCode As Func(Of String, String) =
        Function(raw As String) As String
            If String.IsNullOrWhiteSpace(raw) Then Return Nothing
            Dim s As String = raw.Trim()
            If s.Length = 2 Then Return s.ToUpperInvariant()
            If String.Equals(s, "Illinois", StringComparison.OrdinalIgnoreCase) Then Return "IL"
            If String.Equals(s, "Indiana", StringComparison.OrdinalIgnoreCase) Then Return "IN"
            If String.Equals(s, "Iowa", StringComparison.OrdinalIgnoreCase) Then Return "IA"
            If String.Equals(s, "Missouri", StringComparison.OrdinalIgnoreCase) Then Return "MO"
            If String.Equals(s, "Wisconsin", StringComparison.OrdinalIgnoreCase) Then Return "WI"
            If String.Equals(s, "Kentucky", StringComparison.OrdinalIgnoreCase) Then Return "KY"
            If String.Equals(s, "Michigan", StringComparison.OrdinalIgnoreCase) Then Return "MI"
            If String.Equals(s, "Ohio", StringComparison.OrdinalIgnoreCase) Then Return "OH"
            Return Nothing
        End Function

    Dim ParsePhoneList As Func(Of String, Newtonsoft.Json.Linq.JArray) =
        Function(raw As String) As Newtonsoft.Json.Linq.JArray
            Dim arr As New Newtonsoft.Json.Linq.JArray()
            If String.IsNullOrWhiteSpace(raw) Then Return arr
            Dim parts() As String = System.Text.RegularExpressions.Regex.Split(
                raw.Trim(), "\s*/\s*")
            For Each part As String In parts
                Dim p As String = part.Trim()
                If p.Length = 0 Then Continue For
                Dim label As String = Nothing
                Dim lm As System.Text.RegularExpressions.Match =
                    System.Text.RegularExpressions.Regex.Match(
                        p, "\(([^)]+)\)\s*$")
                If lm.Success AndAlso
                   Not System.Text.RegularExpressions.Regex.IsMatch(
                       lm.Groups(1).Value, "^\d") Then
                    label = lm.Groups(1).Value.Trim()
                    p = p.Substring(0, lm.Index).Trim()
                End If
                Dim nm As System.Text.RegularExpressions.Match =
                    System.Text.RegularExpressions.Regex.Match(
                        p, "(\(\d{3}\)\s*\d{3}-\d{4}(?:\s*x\d+)?)")
                If Not nm.Success Then
                    nm = System.Text.RegularExpressions.Regex.Match(
                        p, "(\d{3}[-.\s]?\d{3}[-.\s]?\d{4})")
                End If
                If Not nm.Success Then Continue For
                Dim num As String = nm.Groups(1).Value.Trim()
                ' normalize to (NNN) NNN-NNNN when possible
                Dim digits As String =
                    System.Text.RegularExpressions.Regex.Replace(num, "[^\d]", "")
                If digits.Length = 10 Then
                    num = "(" & digits.Substring(0, 3) & ") " &
                          digits.Substring(3, 3) & "-" & digits.Substring(6, 4)
                End If
                Dim phone As New Newtonsoft.Json.Linq.JObject()
                phone("number") = New Newtonsoft.Json.Linq.JValue(num)
                If label Is Nothing Then
                    phone("label") = Newtonsoft.Json.Linq.JValue.CreateNull()
                Else
                    phone("label") = New Newtonsoft.Json.Linq.JValue(label)
                End If
                arr.Add(phone)
            Next
            Return arr
        End Function

    Dim PhoneFromLabeledText As Func(Of String, Newtonsoft.Json.Linq.JArray) =
        Function(raw As String) As Newtonsoft.Json.Linq.JArray
            ' e.g. Heather Tatum, daughter (Phone: (217) 619-5832)
            Dim arr As New Newtonsoft.Json.Linq.JArray()
            If String.IsNullOrWhiteSpace(raw) Then Return arr
            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    raw, "\(Phone:\s*([^)]+)\)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            If m.Success Then Return ParsePhoneList(m.Groups(1).Value)
            Return ParsePhoneList(raw)
        End Function

    Dim MaskSsn As Func(Of String, String) =
        Function(raw As String) As String
            If String.IsNullOrWhiteSpace(raw) Then Return Nothing
            Dim s As String = raw.Trim()
            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(s, "(\d{4})\s*$")
            If m.Success Then Return "***-**-" & m.Groups(1).Value
            If System.Text.RegularExpressions.Regex.IsMatch(
                s, "^\*+-\*+-\d{4}$") Then
                Dim last4 As String =
                    System.Text.RegularExpressions.Regex.Match(s, "(\d{4})$").Groups(1).Value
                Return "***-**-" & last4
            End If
            Return Nothing
        End Function

    Dim JNull As Func(Of Newtonsoft.Json.Linq.JToken) =
        Function() As Newtonsoft.Json.Linq.JToken
            Return Newtonsoft.Json.Linq.JValue.CreateNull()
        End Function

    Dim JStr As Func(Of String, Newtonsoft.Json.Linq.JToken) =
        Function(s As String) As Newtonsoft.Json.Linq.JToken
            If s Is Nothing Then Return Newtonsoft.Json.Linq.JValue.CreateNull()
            Return New Newtonsoft.Json.Linq.JValue(s)
        End Function

    Dim JNum As Func(Of Object, Newtonsoft.Json.Linq.JToken) =
        Function(n As Object) As Newtonsoft.Json.Linq.JToken
            If n Is Nothing Then Return Newtonsoft.Json.Linq.JValue.CreateNull()
            Return New Newtonsoft.Json.Linq.JValue(System.Convert.ToDecimal(n))
        End Function

    Dim JBool As Func(Of Object, Newtonsoft.Json.Linq.JToken) =
        Function(b As Object) As Newtonsoft.Json.Linq.JToken
            If b Is Nothing Then Return Newtonsoft.Json.Linq.JValue.CreateNull()
            Return New Newtonsoft.Json.Linq.JValue(CBool(b))
        End Function

    Dim EmptyFrequency As Func(Of Newtonsoft.Json.Linq.JObject) =
        Function() As Newtonsoft.Json.Linq.JObject
            Dim f As New Newtonsoft.Json.Linq.JObject()
            f("hours_per_day") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("hours_per_week") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("hours_per_month") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("visits_per_day") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("visits_per_week") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("visits_per_month") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("units_per_day") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("units_per_week") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("units_per_month") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("installation_required") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("connectivity_type") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("shared_status") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("day_of_week") = New Newtonsoft.Json.Linq.JArray()
            f("other_notes") = Newtonsoft.Json.Linq.JValue.CreateNull()
            Return f
        End Function

    Dim ExtractDonTasks As Func(Of String, Object, Newtonsoft.Json.Linq.JArray) =
        Function(poc As String, hoursPerDayObj As Object) As Newtonsoft.Json.Linq.JArray
            Dim arr As New Newtonsoft.Json.Linq.JArray()
            If String.IsNullOrWhiteSpace(poc) Then Return arr
            Dim hpd As Decimal = 0
            Dim hasHpd As Boolean = False
            If hoursPerDayObj IsNot Nothing Then
                If Decimal.TryParse(
                    hoursPerDayObj.ToString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, hpd) Then
                    hasHpd = True
                End If
            End If
            Dim keyMatches As System.Text.RegularExpressions.MatchCollection =
                System.Text.RegularExpressions.Regex.Matches(
                    poc,
                    "DONScoreView_(\w+)impaiment",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            Dim inhKeys As New System.Collections.Generic.List(Of String)()
            For Each km As System.Text.RegularExpressions.Match In keyMatches
                Dim donKey As String = km.Groups(1).Value
                If inhKeys.Contains(donKey) Then Continue For
                Dim ccpPat As String =
                    "id\s*=\s*""DONScoreView_" &
                    System.Text.RegularExpressions.Regex.Escape(donKey) &
                    "ServiceCCPDescription""[^>]*\bvalue\s*=\s*""INH"""
                If Not System.Text.RegularExpressions.Regex.IsMatch(
                    poc, ccpPat, System.Text.RegularExpressions.RegexOptions.IgnoreCase) Then
                    Continue For
                End If
                inhKeys.Add(donKey)
            Next
            Dim taskCount As Integer = inhKeys.Count
            If taskCount = 0 Then Return arr
            Dim hpdEach As Decimal = If(hasHpd AndAlso hpd > 0, hpd / taskCount, 0)
            For Each donKey As String In inhKeys
                Dim labelPat As String =
                    "<label[^>]*for\s*=\s*""DONScoreView_" &
                    System.Text.RegularExpressions.Regex.Escape(donKey) &
                    "impaiment""[^>]*>([^<]+)</label>"
                Dim lm As System.Text.RegularExpressions.Match =
                    System.Text.RegularExpressions.Regex.Match(
                        poc, labelPat, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                If Not lm.Success Then Continue For
                Dim taskName As String = HtmlDecode(lm.Groups(1).Value).Trim()
                Dim freqPat As String =
                    "id\s*=\s*""DONScoreView_" &
                    System.Text.RegularExpressions.Regex.Escape(donKey) &
                    "Frequency""[^>]*\bvalue\s*=\s*""([^""]*)"""
                Dim fm As System.Text.RegularExpressions.Match =
                    System.Text.RegularExpressions.Regex.Match(
                        poc, freqPat, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                Dim daysWeek As Object = Nothing
                If fm.Success Then daysWeek = ParseNumber(fm.Groups(1).Value)
                Dim t As New Newtonsoft.Json.Linq.JObject()
                t("task") = New Newtonsoft.Json.Linq.JValue(taskName)
                t("hours_per_day") = JNum(If(hasHpd AndAlso hpdEach > 0, CObj(hpdEach), Nothing))
                t("days_per_week") = JNum(daysWeek)
                Dim hpm As Object = Nothing
                If daysWeek IsNot Nothing AndAlso hasHpd AndAlso hpdEach > 0 Then
                    Dim dw As Decimal = CDec(daysWeek)
                    hpm = System.Math.Round(dw * hpdEach * (52D / 12D), 1)
                End If
                t("hours_per_month") = JNum(hpm)
                arr.Add(t)
            Next
            Return arr
        End Function

    Dim ParseTotalCostFromText As Func(Of String, Object) =
        Function(raw As String) As Object
            If String.IsNullOrWhiteSpace(raw) Then Return Nothing
            Dim m As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    raw,
                    "Total\s+cost\s+to\s+state\s*=\s*\$?\s*([\d,]+(?:\.\d+)?)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            If Not m.Success Then Return Nothing
            Return ParseNumber(m.Groups(1).Value)
        End Function

    Dim AuthPeriodEndDate As Func(Of String, String, String) =
        Function(startYmd As String, action As String) As String
            If String.IsNullOrWhiteSpace(startYmd) Then Return Nothing
            Dim dt As System.DateTime
            If Not System.DateTime.TryParseExact(
                startYmd, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, dt) Then
                Return Nothing
            End If
            Dim act As String = If(action, "").Trim().ToUpperInvariant()
            Dim months As Integer = 6
            If act = "INITIAL" Then months = 12
            Dim endDt As System.DateTime = dt.AddMonths(months).AddDays(-1)
            Return endDt.ToString("yyyy-MM-dd")
        End Function

    Dim InferServiceAction As Func(Of String, String, Boolean, String) =
        Function(reason As String, svcType As String, providerSigned As Boolean) As String
            Dim r As String = If(reason, "").Trim()
            Dim st As String = If(svcType, "").Trim()
            If r.Length > 0 AndAlso st.Length > 0 Then
                Dim rLow As String = r.ToLowerInvariant()
                Dim stLow As String = st.ToLowerInvariant()
                Dim verbs() As String = {"continue", "begin", "start", "new", "terminate", "end"}
                For Each verb As String In verbs
                    If Not rLow.Contains(verb) Then Continue For
                    Dim idx As Integer = rLow.IndexOf(verb)
                    Dim chunk As String = rLow.Substring(
                        idx, System.Math.Min(120, rLow.Length - idx))
                    If stLow.Length >= 6 AndAlso chunk.IndexOf(stLow.Substring(0, 6)) >= 0 Then
                        Select Case verb
                            Case "continue"
                                Return "CONTINUE"
                            Case "begin", "start", "new"
                                Return "NEW"
                            Case "terminate", "end"
                                Return "TERMINATE"
                        End Select
                    End If
                    If stLow.IndexOf("in home", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                       (chunk.IndexOf("in home") >= 0 OrElse chunk.IndexOf("hca") >= 0) Then
                        If verb = "continue" Then Return "CONTINUE"
                        If verb = "begin" OrElse verb = "start" OrElse verb = "new" Then Return "NEW"
                    End If
                    If (stLow.IndexOf("emergency") >= 0 OrElse stLow.IndexOf("response") >= 0) AndAlso
                       (chunk.IndexOf("emergency") >= 0 OrElse chunk.IndexOf("ehrs") >= 0) Then
                        If verb = "continue" Then Return "CONTINUE"
                        If verb = "begin" OrElse verb = "start" OrElse verb = "new" Then Return "NEW"
                    End If
                Next
            End If
            If providerSigned Then Return "CONTINUE"
            Return "NEW"
        End Function

    Dim ParseDaysOfWeek As Func(Of String, Newtonsoft.Json.Linq.JArray) =
        Function(raw As String) As Newtonsoft.Json.Linq.JArray
            Dim arr As New Newtonsoft.Json.Linq.JArray()
            If String.IsNullOrWhiteSpace(raw) Then Return arr
            Dim map As New System.Collections.Generic.Dictionary(
                Of String, String)(StringComparer.OrdinalIgnoreCase)
            map("monday") = "MON"
            map("mon") = "MON"
            map("tuesday") = "TUE"
            map("tue") = "TUE"
            map("wednesday") = "WED"
            map("wed") = "WED"
            map("thursday") = "THU"
            map("thu") = "THU"
            map("friday") = "FRI"
            map("fri") = "FRI"
            map("saturday") = "SAT"
            map("sat") = "SAT"
            map("sunday") = "SUN"
            map("sun") = "SUN"
            For Each part As String In raw.Split(
                New Char() {","c, ";"c, "|"c, "/"c, " "c},
                System.StringSplitOptions.RemoveEmptyEntries)
                Dim p As String = part.Trim()
                If map.ContainsKey(p) Then
                    Dim code As String = map(p)
                    Dim exists As Boolean = False
                    For Each tok As Newtonsoft.Json.Linq.JToken In arr
                        If String.Equals(CStr(tok), code, StringComparison.OrdinalIgnoreCase) Then
                            exists = True
                            Exit For
                        End If
                    Next
                    If Not exists Then arr.Add(New Newtonsoft.Json.Linq.JValue(code))
                End If
            Next
            Return arr
        End Function

    Dim ExtractDiagnoses As Func(Of String, Newtonsoft.Json.Linq.JArray) =
        Function(allHtml As String) As Newtonsoft.Json.Linq.JArray
            Dim arr As New Newtonsoft.Json.Linq.JArray()
            If String.IsNullOrWhiteSpace(allHtml) Then Return arr
            Dim seen As New System.Collections.Generic.HashSet(Of String)(
                StringComparer.OrdinalIgnoreCase)
            Dim rowMatches As System.Text.RegularExpressions.MatchCollection =
                System.Text.RegularExpressions.Regex.Matches(
                    allHtml,
                    "<td[^>]*>\s*(\d+)\s*</td>\s*<td[^>]*>\s*([A-TV-Z]\d{2}(?:\.\d+)?)\s*</td>\s*<td[^>]*>([^<]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            For Each rm As System.Text.RegularExpressions.Match In rowMatches
                Dim code As String = rm.Groups(2).Value.Trim()
                If seen.Contains(code) Then Continue For
                seen.Add(code)
                Dim d As New Newtonsoft.Json.Linq.JObject()
                d("RANK") = New Newtonsoft.Json.Linq.JValue(Integer.Parse(rm.Groups(1).Value))
                d("CODE") = New Newtonsoft.Json.Linq.JValue(code)
                d("DESCRIPTION") = New Newtonsoft.Json.Linq.JValue(
                    HtmlDecode(rm.Groups(3).Value).Trim())
                arr.Add(d)
            Next
            Return arr
        End Function

    ' ---------- load HTMLs ----------
    Dim pocHtml As String = Nothing
    Dim hubHtml As String = Nothing
    Dim pocPath As String = Nothing
    Dim hubPath As String = Nothing
    Dim supplementalHtml As New System.Collections.Generic.List(Of String)()

    For Each pathItem As String In htmlPaths
        Dim p As String = If(pathItem, "").Trim()
        If p.Length = 0 Then Continue For
        If Not System.IO.File.Exists(p) Then
            Console.WriteLine("SKIP missing file: " & p)
            Continue For
        End If
        Dim name As String = System.IO.Path.GetFileName(p)
        Dim html As String = System.IO.File.ReadAllText(p)
        Dim isPoc As Boolean =
            name.IndexOf("ViewPlanOfCare", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            name.IndexOf("PlanOfCare", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            html.IndexOf("Person - Centered Plan of Care", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            html.IndexOf("Services_0__ServiceTypeDescription", StringComparison.OrdinalIgnoreCase) >= 0
        Dim isHub As Boolean =
            name.IndexOf("ViewProviderParticipantHub", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            name.IndexOf("ParticipantHub", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            (html.IndexOf("ECCPIS " & ChrW(35), StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
             html.IndexOf("Participant CCP Services", StringComparison.OrdinalIgnoreCase) >= 0)

        If isPoc AndAlso pocHtml Is Nothing Then
            pocHtml = html
            pocPath = p
            Console.WriteLine("POC HTML: " & p)
        ElseIf isHub AndAlso hubHtml Is Nothing Then
            hubHtml = html
            hubPath = p
            Console.WriteLine("Hub HTML: " & p)
        Else
            supplementalHtml.Add(html)
            Console.WriteLine("Supplemental HTML: " & p)
        End If
    Next

    If pocHtml Is Nothing Then
        Throw New System.Exception(
            "No ViewPlanOfCare HTML found in htmlPaths. Archive Plan of Care first.")
    End If

    Dim combinedHtml As String = pocHtml
    If hubHtml IsNot Nothing Then combinedHtml &= hubHtml
    For Each sup As String In supplementalHtml
        combinedHtml &= sup
    Next

    Dim pocLines As System.Collections.Generic.List(Of String) = VisibleLines(pocHtml)
    Dim hubLines As System.Collections.Generic.List(Of String) =
        If(hubHtml Is Nothing, New System.Collections.Generic.List(Of String)(), VisibleLines(hubHtml))

    ' ---------- extract POC fields ----------
    Dim participantName As String = GetInputValue(pocHtml, "ParticipantName")
    Dim address1 As String = GetInputValue(pocHtml, "AddressLine1")
    Dim address2 As String = GetInputValue(pocHtml, "AddressLine2")
    Dim address3 As String = GetInputValue(pocHtml, "AddressLine3")
    Dim assessmentCcu As String = GetInputValue(pocHtml, "AssessmentCCU")
    Dim ccuPhone As String = GetInputValue(pocHtml, "CCUPhoneNumber")
    Dim eligNotify As String = GetInputValue(pocHtml, "EligibilityNotificationDate")
    Dim eligDeterm As String = GetInputValue(pocHtml, "EligibilityDeterminationDate")
    Dim authType As String = GetInputValue(pocHtml, "AuthorizationType")
    Dim eligAssess As String = GetInputValue(pocHtml, "EligibilityAssessmentDescription")
    Dim eligAssessTail As String =
        GetInputValue(pocHtml, "EligibilityAssessmentTerminationDenialDescription")
    Dim notes As String = GetInputValue(pocHtml, "Notes")
    Dim careCoordinator As String = GetInputValue(pocHtml, "CareCoordinator")
    Dim participantSigDate As String = GetInputValue(pocHtml, "participantdate")
    Dim ccuSigDate As String = GetInputValue(pocHtml, "ccudate")
    Dim participantSig As String = GetInputValue(pocHtml, "participantsignature")
    Dim ccuSig As String = GetInputValue(pocHtml, "ccusignature")
    Dim assistingPerson As String =
        GetInputValue(pocHtml, "AgreementAssistingPersonDescription")
    Dim assistingRel As String =
        GetInputValue(pocHtml, "AgreementAssistingPersonRelationshipDescription")

    Dim participantId As String = LineAfterLabel(pocLines, "Participant Id")
    Dim eligFindingVisible As String = LineAfterLabel(pocLines, "Eligibility Finding:")
    Dim eligReasonVisible As String = LineAfterLabel(pocLines, "Reason:")
    Dim specialInstrVisible As String =
        LineAfterLabel(pocLines, "Notes/Special Instructions:")

    Dim eligibilityFinding As String = Nothing
    If Not String.IsNullOrWhiteSpace(eligAssess) OrElse
       Not String.IsNullOrWhiteSpace(eligAssessTail) Then
        eligibilityFinding =
            (If(eligAssess, "") & If(eligAssessTail, "")).Trim()
        If eligibilityFinding.StartsWith(",") Then
            eligibilityFinding = eligibilityFinding.Substring(1).Trim()
        End If
    End If
    If String.IsNullOrWhiteSpace(eligibilityFinding) Then
        eligibilityFinding = eligFindingVisible
    End If

    Dim eligibilityReason As String = eligReasonVisible
    Dim specialInstructions As String =
        If(Not String.IsNullOrWhiteSpace(notes), notes, specialInstrVisible)

    ' Auth status / action
    Dim authStatus As String = Nothing
    Dim findingLow As String = If(eligibilityFinding, "").ToLowerInvariant()
    If findingLow.Contains("approved") Then
        authStatus = "APPROVED"
    ElseIf findingLow.Contains("denied") OrElse findingLow.Contains("denial") Then
        authStatus = "DENIED"
    ElseIf findingLow.Contains("terminat") Then
        authStatus = "CANCELLED"
    ElseIf findingLow.Contains("pend") Then
        authStatus = "PENDED"
    End If

    Dim authAction As String = Nothing
    Dim assessType As String = Nothing
    Dim authTypeUp As String = If(authType, "").Trim().ToUpperInvariant()
    If authTypeUp = "RED" OrElse authTypeUp.StartsWith("RED") Then
        authAction = "REDETERMINATION"
        assessType = "Redetermination Assessment"
    ElseIf authTypeUp = "INI" OrElse authTypeUp = "INIT" OrElse authTypeUp = "INITIAL" Then
        authAction = "INITIAL"
        assessType = "Initial Assessment"
    ElseIf authTypeUp = "CHG" OrElse authTypeUp = "CHANGE" Then
        authAction = "CHANGE"
    ElseIf authTypeUp = "TERM" OrElse authTypeUp = "TERMINATION" Then
        authAction = "TERMINATION"
    End If
    Dim assessDateFromText As String = Nothing
    Dim adm As System.Text.RegularExpressions.Match =
        System.Text.RegularExpressions.Regex.Match(
            If(eligibilityFinding, ""),
            "Assessment on (\d{1,2}/\d{1,2}/\d{4})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
    If adm.Success Then assessDateFromText = ParseDateYmd(adm.Groups(1).Value)
    Dim assessmentDate As String =
        If(assessDateFromText, ParseDateYmd(eligDeterm))

    Dim authDateReceived As String = ParseDateYmd(eligNotify)

    Dim authStartDate As String = ParseDateYmd(eligDeterm)
    If authStartDate Is Nothing Then authStartDate = authDateReceived

    Dim totalPlanCost As Object =
        ParseTotalCostFromText(eligibilityReason)
    If totalPlanCost Is Nothing Then
        totalPlanCost = ParseTotalCostFromText(eligibilityFinding)
    End If

    ' Address split from AddressLine3 "City, State Zip"
    Dim city As String = Nothing
    Dim stateCode As String = Nothing
    Dim zip As String = Nothing
    If Not String.IsNullOrWhiteSpace(address3) Then
        Dim am As System.Text.RegularExpressions.Match =
            System.Text.RegularExpressions.Regex.Match(
                address3.Trim(),
                "^(.+?),\s*([A-Za-z .]+)\s+(\d{5}(?:-\d{4})?)$")
        If am.Success Then
            city = am.Groups(1).Value.Trim()
            stateCode = StateToCode(am.Groups(2).Value.Trim())
            zip = am.Groups(3).Value.Trim()
        End If
    End If

    Dim addressFullParts As New System.Collections.Generic.List(Of String)()
    If Not String.IsNullOrWhiteSpace(address1) Then addressFullParts.Add(address1.Trim())
    If Not String.IsNullOrWhiteSpace(address2) Then addressFullParts.Add(address2.Trim())
    If Not String.IsNullOrWhiteSpace(address3) Then addressFullParts.Add(address3.Trim())
    Dim addressFull As String =
        If(addressFullParts.Count > 0, String.Join(", ", addressFullParts), Nothing)

    ' Emergency contacts from POC visible lines
    Dim emergencyArr As New Newtonsoft.Json.Linq.JArray()
    Dim inEmerg As Boolean = False
    For Each ln As String In pocLines
        If String.Equals(ln, "Emergency Contacts:", StringComparison.OrdinalIgnoreCase) OrElse
           String.Equals(ln, "Emergency Contacts", StringComparison.OrdinalIgnoreCase) Then
            inEmerg = True
            Continue For
        End If
        If inEmerg Then
            If ln.EndsWith(":") OrElse
               ln.StartsWith("Areas of Assistance", StringComparison.OrdinalIgnoreCase) OrElse
               ln.StartsWith("ADLs", StringComparison.OrdinalIgnoreCase) OrElse
               ln.StartsWith("Service By", StringComparison.OrdinalIgnoreCase) Then
                Exit For
            End If
            Dim em As System.Text.RegularExpressions.Match =
                System.Text.RegularExpressions.Regex.Match(
                    ln, "^(.+?),\s*([^(]+?)\s*\(Phone:\s*([^)]+)\)\s*$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            If em.Success Then
                Dim ec As New Newtonsoft.Json.Linq.JObject()
                ec("name") = em.Groups(1).Value.Trim()
                ec("relationship") = em.Groups(2).Value.Trim()
                ec("phones") = ParsePhoneList(em.Groups(3).Value)
                emergencyArr.Add(ec)
            End If
        End If
    Next

    ' ---------- Hub participant fields ----------
    Dim idoaId As String = Nothing
    Dim rin As String = Nothing
    Dim ssnMasked As String = Nothing
    Dim gender As String = Nothing
    Dim dob As String = Nothing
    Dim primaryLanguage As String = Nothing
    Dim participantPhoneRaw As String = Nothing
    Dim ccuContract As String = Nothing
    Dim hubCcuName As String = Nothing

    If hubLines.Count > 0 Then
        Dim hi As Integer
        For hi = 0 To hubLines.Count - 1
            Dim hubLine As String = hubLines(hi)
            Dim hubLow As String = hubLine.ToLowerInvariant()

            If hubLow.StartsWith("eccpis") AndAlso hubLine.IndexOf(":"c) >= 0 Then
                idoaId = hubLine.Substring(hubLine.IndexOf(":"c) + 1).Trim()
            End If
            If hubLow.StartsWith("rin") AndAlso hubLine.IndexOf(":"c) >= 0 Then
                rin = hubLine.Substring(hubLine.IndexOf(":"c) + 1).Trim()
            End If
            If hubLow.StartsWith("ssn:") Then
                ssnMasked = MaskSsn(hubLine.Substring(hubLine.IndexOf(":"c) + 1).Trim())
            End If
            If String.IsNullOrWhiteSpace(participantId) AndAlso
               hubLine.IndexOf(":"c) >= 0 AndAlso
               (hubLow.StartsWith("id " & ChrW(35)) OrElse hubLow.StartsWith("id:")) Then
                participantId = hubLine.Substring(hubLine.IndexOf(":"c) + 1).Trim()
            End If

            Dim langPrefix As String = "language of choice is "
            If hubLow.StartsWith(langPrefix) Then
                primaryLanguage = hubLine.Substring(langPrefix.Length).Trim()
            End If

            If hubLow.StartsWith("male") OrElse hubLow.StartsWith("female") OrElse
               hubLow.StartsWith("other") Then
                If hubLine.IndexOf("Born on", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   hubLine.IndexOf("|"c) >= 0 Then
                    Dim pipePos As Integer = hubLine.IndexOf("|"c)
                    If pipePos > 0 Then
                        gender = hubLine.Substring(0, pipePos).Trim()
                    End If
                End If
            End If

            If gender IsNot Nothing AndAlso dob Is Nothing Then
                If hubLine.Length >= 8 AndAlso Char.IsDigit(hubLine.Chars(0)) Then
                    Dim slash1 As Integer = hubLine.IndexOf("/"c)
                    If slash1 > 0 Then
                        dob = ParseDateYmd(hubLine)
                    End If
                End If
            End If

            If participantPhoneRaw Is Nothing AndAlso
               hubLine.StartsWith("("c) AndAlso hubLine.IndexOf(")"c) > 0 Then
                participantPhoneRaw = hubLine
            End If
        Next

        If String.IsNullOrWhiteSpace(participantName) Then
            For i As Integer = 0 To hubLines.Count - 1
                If hubLines(i).IndexOf("Navigation", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                   i + 1 < hubLines.Count Then
                    Dim cand As String = hubLines(i + 1)
                    If System.Text.RegularExpressions.Regex.IsMatch(
                        cand, "^[A-Za-z][A-Za-z\s'\-\.]+$") AndAlso
                       cand.IndexOf(":"c) < 0 Then
                        participantName = cand
                        Exit For
                    End If
                End If
            Next
        End If

        hubCcuName = LineAfterLabel(hubLines, "Care Coordinator Unit Name")
        ccuContract = LineAfterLabel(hubLines, "CCU Contract Number")

        If String.IsNullOrWhiteSpace(addressFull) Then
            For Each ln As String In hubLines
                Dim hm2 As System.Text.RegularExpressions.Match =
                    System.Text.RegularExpressions.Regex.Match(
                        ln,
                        "^(.+?),\s*(.+?),\s*([A-Za-z .]+)\s+(\d{5}(?:-\d{4})?)$")
                If hm2.Success Then
                    addressFull = ln
                    address1 = hm2.Groups(1).Value.Trim()
                    city = hm2.Groups(2).Value.Trim()
                    stateCode = StateToCode(hm2.Groups(3).Value.Trim())
                    zip = hm2.Groups(4).Value.Trim()
                    Exit For
                End If
            Next
        End If
    End If

    For Each supHtml As String In supplementalHtml
        If String.IsNullOrWhiteSpace(gender) Then
            gender = GetInputValue(supHtml, "GenderDescription")
        End If
        If dob Is Nothing Then
            dob = ParseDateYmd(GetInputValue(supHtml, "DOB"))
        End If
        If String.IsNullOrWhiteSpace(participantName) Then
            participantName = GetInputValue(supHtml, "FullName")
        End If
        If String.IsNullOrWhiteSpace(addressFull) Then
            Dim fullAddr As String = GetInputValue(supHtml, "FullAddress")
            If Not String.IsNullOrWhiteSpace(fullAddr) Then
                addressFull = fullAddr
                Dim am3 As System.Text.RegularExpressions.Match =
                    System.Text.RegularExpressions.Regex.Match(
                        fullAddr.Trim(),
                        "^(.+?),\s*(.+?),\s*([A-Za-z .]+)\s+(\d{5}(?:-\d{4})?)$")
                If am3.Success Then
                    address1 = am3.Groups(1).Value.Trim()
                    city = am3.Groups(2).Value.Trim()
                    stateCode = StateToCode(am3.Groups(3).Value.Trim())
                    zip = am3.Groups(4).Value.Trim()
                End If
            End If
        End If
    Next

    Dim interpreterNeeded As Object = Nothing
    Dim interpMatch As System.Text.RegularExpressions.Match =
        System.Text.RegularExpressions.Regex.Match(
            combinedHtml,
            "Interpreter[^<]{0,80}?(Yes|No|Y|N)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
    If interpMatch.Success Then
        interpreterNeeded = YnToBool(interpMatch.Groups(1).Value)
    ElseIf Not String.IsNullOrWhiteSpace(primaryLanguage) AndAlso
           primaryLanguage.IndexOf("English", StringComparison.OrdinalIgnoreCase) >= 0 Then
        interpreterNeeded = False
    End If

    If String.IsNullOrWhiteSpace(assessmentCcu) AndAlso
       Not String.IsNullOrWhiteSpace(hubCcuName) Then
        assessmentCcu = hubCcuName
    End If

    Dim authEndDate As String = AuthPeriodEndDate(authStartDate, authAction)

    ' ---------- Services ----------
    Dim servicesArr As New Newtonsoft.Json.Linq.JArray()
    Dim signaturesArr As New Newtonsoft.Json.Linq.JArray()

    ' Participant / AR signature
    Dim partSigPresent As Boolean =
        Not String.IsNullOrWhiteSpace(participantSig) AndAlso
        participantSig.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
    Dim partSigObj As New Newtonsoft.Json.Linq.JObject()
    If Not String.IsNullOrWhiteSpace(assistingPerson) Then
        partSigObj("signer_role") = New Newtonsoft.Json.Linq.JValue("AUTHORIZED_REPRESENTATIVE")
        partSigObj("signer_name") = JStr(assistingPerson)
    Else
        partSigObj("signer_role") = New Newtonsoft.Json.Linq.JValue("PARTICIPANT")
        partSigObj("signer_name") = JStr(participantName)
    End If
    partSigObj("signature_present") = New Newtonsoft.Json.Linq.JValue(partSigPresent)
    If partSigPresent Then
        partSigObj("signature_date") = JStr(ParseDateYmd(participantSigDate))
    Else
        partSigObj("signature_date") = JNull()
    End If
    partSigObj("provider_name") = JNull()
    partSigObj("service_type") = JNull()
    partSigObj("service_line_number") = JNull()
    signaturesArr.Add(partSigObj)

    ' Care coordinator signature
    Dim ccuSigPresent As Boolean =
        Not String.IsNullOrWhiteSpace(ccuSig) AndAlso
        ccuSig.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
    Dim ccSigObj As New Newtonsoft.Json.Linq.JObject()
    ccSigObj("signer_role") = New Newtonsoft.Json.Linq.JValue("CARE_COORDINATOR")
    ccSigObj("signer_name") = JStr(careCoordinator)
    ccSigObj("signature_present") = New Newtonsoft.Json.Linq.JValue(ccuSigPresent)
    If ccuSigPresent Then
        ccSigObj("signature_date") = JStr(ParseDateYmd(ccuSigDate))
    Else
        ccSigObj("signature_date") = JNull()
    End If
    ccSigObj("provider_name") = JNull()
    ccSigObj("service_type") = JNull()
    ccSigObj("service_line_number") = JNull()
    signaturesArr.Add(ccSigObj)

    Dim maxSvc As Integer = 40
    For i As Integer = 0 To maxSvc
        Dim prefix As String = "Services_" & i.ToString() & "__"
        Dim svcType As String = GetInputValue(pocHtml, prefix & "ServiceTypeDescription")
        If String.IsNullOrWhiteSpace(svcType) Then
            If i = 0 Then Continue For
            Exit For
        End If

        Dim providerName As String = GetInputValue(pocHtml, prefix & "ProviderName")
        Dim providerPhone As String = GetInputValue(pocHtml, prefix & "ProviderPhoneNumber")
        Dim hoursDay As String = GetInputValue(pocHtml, prefix & "ServiceHoursPerDay")
        Dim timesWeek As String = GetInputValue(pocHtml, prefix & "ServiceTimesPerWeek")
        Dim hoursWeek As String = GetInputValue(pocHtml, prefix & "ServiceHoursPerWeek")
        Dim installation As String = GetInputValue(pocHtml, prefix & "Installation")
        Dim monthlyShares As String = GetInputValue(pocHtml, prefix & "MonthlyShares")
        Dim connectionType As String = GetInputValue(pocHtml, prefix & "ConnectionType")
        Dim daysOfWeekRaw As String = GetInputValue(pocHtml, prefix & "DaysOfWeek")
        Dim providerLocation As String = GetInputValue(pocHtml, prefix & "ProviderLocation")
        Dim svcStartHidden As String = GetInputValue(pocHtml, prefix & "ServiceStartDate")
        Dim provSig As String = GetInputValue(pocHtml, prefix & "ProviderSignature")
        Dim provSigDate As String = GetInputValue(pocHtml, prefix & "ProviderSignatureDate")

        ' Visible supplements: Referral Date / On or Before Start Date after service type
        Dim referralDate As String = Nothing
        Dim onOrBeforeStart As String = Nothing
        Dim foundType As Integer = -1
        For li As Integer = 0 To pocLines.Count - 1
            If String.Equals(pocLines(li), svcType, StringComparison.OrdinalIgnoreCase) Then
                ' prefer occurrence near provider name
                If Not String.IsNullOrWhiteSpace(providerName) AndAlso
                   li + 1 < pocLines.Count AndAlso
                   pocLines(li + 1).IndexOf(providerName.Substring(0, System.Math.Min(12, providerName.Length)), StringComparison.OrdinalIgnoreCase) >= 0 Then
                    foundType = li
                    Exit For
                End If
                If foundType < 0 Then foundType = li
            End If
        Next
        If foundType >= 0 Then
            For li As Integer = foundType To System.Math.Min(foundType + 25, pocLines.Count - 2)
                If String.Equals(pocLines(li), "Referral Date", StringComparison.OrdinalIgnoreCase) Then
                    referralDate = ParseDateYmd(pocLines(li + 1))
                End If
                If String.Equals(pocLines(li), "On or Before Start Date", StringComparison.OrdinalIgnoreCase) Then
                    onOrBeforeStart = ParseDateYmd(pocLines(li + 1))
                End If
                If String.Equals(pocLines(li), "Anticipated Start Date", StringComparison.OrdinalIgnoreCase) AndAlso
                   onOrBeforeStart Is Nothing Then
                    onOrBeforeStart = ParseDateYmd(pocLines(li + 1))
                End If
            Next
        End If

        Dim svcStart As String = ParseDateYmd(svcStartHidden)
        If svcStart Is Nothing Then svcStart = onOrBeforeStart

        Dim freq As Newtonsoft.Json.Linq.JObject = EmptyFrequency()
        freq("hours_per_day") = JNum(ParseNumber(hoursDay))
        freq("hours_per_week") = JNum(ParseNumber(hoursWeek))
        freq("visits_per_week") = JNum(ParseNumber(timesWeek))

        ' EHRS / device fields
        Dim isDevice As Boolean =
            svcType.IndexOf("Emergency", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            svcType.IndexOf("Response", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            svcType.IndexOf("Medication", StringComparison.OrdinalIgnoreCase) >= 0

        If isDevice OrElse Not String.IsNullOrWhiteSpace(monthlyShares) Then
            Dim shares As String = If(monthlyShares, "")
            If Not String.Equals(shares, "N", StringComparison.OrdinalIgnoreCase) AndAlso
               shares.Length > 0 Then
                freq("visits_per_month") = JNum(ParseNumber(shares))
                    If shares.IndexOf("Unshared", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    freq("shared_status") = New Newtonsoft.Json.Linq.JValue("UNSHARED")
                ElseIf shares.IndexOf("Shared", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    freq("shared_status") = New Newtonsoft.Json.Linq.JValue("SHARED")
                End If
            End If
        End If

        If isDevice Then
            freq("installation_required") = JBool(YnToBool(installation))
            If Not String.IsNullOrWhiteSpace(connectionType) AndAlso
               Not String.Equals(connectionType, "N", StringComparison.OrdinalIgnoreCase) Then
                freq("connectivity_type") = New Newtonsoft.Json.Linq.JValue(connectionType)
            End If
        End If

        Dim dayArr As Newtonsoft.Json.Linq.JArray = ParseDaysOfWeek(daysOfWeekRaw)
        If dayArr.Count = 0 AndAlso foundType >= 0 Then
            For li As Integer = foundType To System.Math.Min(foundType + 30, pocLines.Count - 1)
                If pocLines(li).IndexOf("Day of Week", StringComparison.OrdinalIgnoreCase) >= 0 AndAlso
                   li + 1 < pocLines.Count Then
                    dayArr = ParseDaysOfWeek(pocLines(li + 1))
                    If dayArr.Count > 0 Then Exit For
                End If
            Next
        End If
        freq("day_of_week") = dayArr

        Dim lineNum As Integer = i + 1
        Dim provPresentForAction As Boolean =
            Not String.IsNullOrWhiteSpace(provSig) AndAlso
            provSig.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        Dim svcAction As String =
            InferServiceAction(eligibilityReason, svcType, provPresentForAction)

        Dim hourlyRate As Newtonsoft.Json.Linq.JToken = JNull()
        Dim rateRaw As String = GetInputValue(pocHtml, prefix & "HourlyRate")
        If String.IsNullOrWhiteSpace(rateRaw) Then
            rateRaw = GetInputValue(pocHtml, prefix & "ProviderRate")
        End If
        If Not String.IsNullOrWhiteSpace(rateRaw) Then
            hourlyRate = JNum(ParseNumber(rateRaw))
        End If

        Dim tasksArr As Newtonsoft.Json.Linq.JArray = New Newtonsoft.Json.Linq.JArray()
        If svcType.IndexOf("In Home", StringComparison.OrdinalIgnoreCase) >= 0 Then
            tasksArr = ExtractDonTasks(pocHtml, ParseNumber(hoursDay))
        End If

        Dim svcObj As New Newtonsoft.Json.Linq.JObject()
        svcObj("line_number") = New Newtonsoft.Json.Linq.JValue(lineNum)
        svcObj("service_type") = New Newtonsoft.Json.Linq.JValue(svcType)
        svcObj("service_action") = JStr(svcAction)
        svcObj("hourly_rate") = hourlyRate
        svcObj("provider_name") = JStr(providerName)
        svcObj("provider_location") = JStr(providerLocation)
        svcObj("provider_phones") = ParsePhoneList(providerPhone)
        svcObj("referral_date") = JStr(referralDate)
        svcObj("service_start_date") = JStr(svcStart)
        svcObj("service_end_date") = JNull()
        svcObj("frequency") = freq
        svcObj("tasks") = tasksArr
        servicesArr.Add(svcObj)

        ' Provider signature block
        Dim provPresent As Boolean =
            Not String.IsNullOrWhiteSpace(provSig) AndAlso
            provSig.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        Dim provSignerName As String = Nothing
        Dim sigObj As New Newtonsoft.Json.Linq.JObject()
        sigObj("signer_role") = New Newtonsoft.Json.Linq.JValue("PROVIDER")
        sigObj("signer_name") = JStr(provSignerName)
        sigObj("signature_present") = New Newtonsoft.Json.Linq.JValue(provPresent)
        If provPresent Then
            sigObj("signature_date") = JStr(ParseDateYmd(provSigDate))
        Else
            sigObj("signature_date") = JNull()
        End If
        sigObj("provider_name") = JStr(providerName)
        sigObj("service_type") = New Newtonsoft.Json.Linq.JValue(svcType)
        sigObj("service_line_number") = New Newtonsoft.Json.Linq.JValue(lineNum)
        signaturesArr.Add(sigObj)
    Next

    If servicesArr.Count = 0 Then
        Throw New System.Exception("No service_authorizations_summary found in Plan of Care HTML.")
    End If

    ' Provider SIGNER_NAME is printed after "Authorized Signature/Date" under the service type
    For Each sigTok As Newtonsoft.Json.Linq.JToken In signaturesArr
        Dim sigObj As Newtonsoft.Json.Linq.JObject =
            CType(sigTok, Newtonsoft.Json.Linq.JObject)
        If Not String.Equals(CStr(sigObj("signer_role")), "PROVIDER") Then Continue For
        If sigObj("signer_name").Type <> Newtonsoft.Json.Linq.JTokenType.Null Then Continue For
        Dim st As String = CStr(sigObj("service_type"))
        For li As Integer = 0 To pocLines.Count - 2
            If Not String.Equals(pocLines(li), st, StringComparison.OrdinalIgnoreCase) Then
                Continue For
            End If
            If li + 1 >= pocLines.Count OrElse
               Not String.Equals(
                   pocLines(li + 1), "Authorized Signature/Date",
                   StringComparison.OrdinalIgnoreCase) Then
                Continue For
            End If
            If li + 2 < pocLines.Count Then
                Dim cand As String = pocLines(li + 2)
                If System.Text.RegularExpressions.Regex.IsMatch(
                    cand, "^[A-Z][a-zA-Z'\-]+(?:\s+[A-Z][a-zA-Z'\-]+)+$") Then
                    sigObj("signer_name") = cand
                End If
            End If
        Next
    Next

    ' ATTENDING / REFERRING
    Dim attending As String = Nothing
    If servicesArr.Count > 0 Then
        Dim firstProv As Newtonsoft.Json.Linq.JToken = servicesArr(0)("provider_name")
        If firstProv IsNot Nothing AndAlso firstProv.Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
            attending = CStr(firstProv)
        End If
    End If
    Dim referring As String = careCoordinator

    Dim authNoteParts As New System.Collections.Generic.List(Of String)()
    If authStartDate IsNot Nothing AndAlso authEndDate IsNot Nothing Then
        authNoteParts.Add(
            "Authorization valid " & authStartDate & " through " & authEndDate)
    End If
    For Each svcTok As Newtonsoft.Json.Linq.JToken In servicesArr
        Dim so As Newtonsoft.Json.Linq.JObject = CType(svcTok, Newtonsoft.Json.Linq.JObject)
        Dim bit As String = CStr(so("service_type"))
        Dim actTok As Newtonsoft.Json.Linq.JToken = so("service_action")
        If actTok IsNot Nothing AndAlso actTok.Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
            bit = CStr(actTok) & " " & bit
        End If
        Dim f As Newtonsoft.Json.Linq.JObject = CType(so("frequency"), Newtonsoft.Json.Linq.JObject)
        Dim hpw As Newtonsoft.Json.Linq.JToken = f("hours_per_week")
        Dim hpd As Newtonsoft.Json.Linq.JToken = f("hours_per_day")
        Dim vpw As Newtonsoft.Json.Linq.JToken = f("visits_per_week")
        Dim vpm As Newtonsoft.Json.Linq.JToken = f("visits_per_month")
        If hpd.Type <> Newtonsoft.Json.Linq.JTokenType.Null AndAlso
           vpw.Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
            bit &= ": " & hpd.ToString() & " hrs x " & vpw.ToString() & "/wk"
        ElseIf hpw.Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
            bit &= ": " & hpw.ToString() & " hrs/week"
        ElseIf vpm.Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
            bit &= ": " & vpm.ToString() & " monthly"
            If f("connectivity_type").Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
                bit &= ", " & f("connectivity_type").ToString()
            End If
            If f("shared_status").Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
                bit &= ", " & f("shared_status").ToString()
            End If
        End If
        authNoteParts.Add(bit)
    Next
    Dim authNote As String =
        If(authNoteParts.Count > 0, String.Join("; ", authNoteParts), Nothing)

    ' Authorized representative object
    Dim authRep As Newtonsoft.Json.Linq.JToken = Newtonsoft.Json.Linq.JValue.CreateNull()
    If Not String.IsNullOrWhiteSpace(assistingPerson) Then
        Dim ar As New Newtonsoft.Json.Linq.JObject()
        ar("name") = assistingPerson
        ar("relationship") = JStr(assistingRel)
        Dim arPhones As New Newtonsoft.Json.Linq.JArray()
        For Each ecTok As Newtonsoft.Json.Linq.JToken In emergencyArr
            Dim ec As Newtonsoft.Json.Linq.JObject = CType(ecTok, Newtonsoft.Json.Linq.JObject)
            Dim ecName As String = CStr(ec("name"))
            If ecName IsNot Nothing AndAlso
               ecName.Equals(assistingPerson, StringComparison.OrdinalIgnoreCase) Then
                arPhones = CType(ec("phones"), Newtonsoft.Json.Linq.JArray)
                Exit For
            End If
        Next
        ar("phones") = arPhones
        authRep = ar
    End If

    Dim diagnosesArr As Newtonsoft.Json.Linq.JArray = ExtractDiagnoses(combinedHtml)

    Dim primaryIcdCode As String = Nothing
    Dim primaryIcdDesc As String = Nothing
    Dim sec1Code As String = Nothing
    Dim sec1Desc As String = Nothing
    Dim sec2Code As String = Nothing
    Dim sec2Desc As String = Nothing
    Dim sec3Code As String = Nothing
    Dim sec3Desc As String = Nothing
    Dim dxSorted As New System.Collections.Generic.List(Of Newtonsoft.Json.Linq.JObject)()
    For Each dxTok As Newtonsoft.Json.Linq.JToken In diagnosesArr
        dxSorted.Add(CType(dxTok, Newtonsoft.Json.Linq.JObject))
    Next
    dxSorted.Sort(
        Function(a As Newtonsoft.Json.Linq.JObject, b As Newtonsoft.Json.Linq.JObject) As Integer
            Dim ra As Integer = 999
            Dim rb As Integer = 999
            If a("RANK") IsNot Nothing AndAlso
               a("RANK").Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
                ra = CInt(a("RANK"))
            End If
            If b("RANK") IsNot Nothing AndAlso
               b("RANK").Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
                rb = CInt(b("RANK"))
            End If
            Return ra.CompareTo(rb)
        End Function)
    If dxSorted.Count > 0 Then
        primaryIcdCode = CStr(dxSorted(0)("CODE"))
        primaryIcdDesc = CStr(dxSorted(0)("DESCRIPTION"))
    End If
    If dxSorted.Count > 1 Then
        sec1Code = CStr(dxSorted(1)("CODE"))
        sec1Desc = CStr(dxSorted(1)("DESCRIPTION"))
    End If
    If dxSorted.Count > 2 Then
        sec2Code = CStr(dxSorted(2)("CODE"))
        sec2Desc = CStr(dxSorted(2)("DESCRIPTION"))
    End If
    If dxSorted.Count > 3 Then
        sec3Code = CStr(dxSorted(3)("CODE"))
        sec3Desc = CStr(dxSorted(3)("DESCRIPTION"))
    End If

    Dim assessmentRecordId As String = GetInputValue(pocHtml, "AssessmentId")

    Dim payorName As String = Nothing
    If pocHtml.IndexOf("Community Care Program", StringComparison.OrdinalIgnoreCase) >= 0 Then
        If Not String.IsNullOrWhiteSpace(assessmentCcu) Then
            payorName = assessmentCcu & " / Community Care Program"
        Else
            payorName = "Community Care Program"
        End If
    ElseIf Not String.IsNullOrWhiteSpace(assessmentCcu) Then
        payorName = assessmentCcu
    End If

    ' ---------- assemble root ----------
    Dim root As New Newtonsoft.Json.Linq.JObject()
    root("schema_version") = New Newtonsoft.Json.Linq.JValue("2.0")

    Dim sourceObj As New Newtonsoft.Json.Linq.JObject()
    sourceObj("bucket") = New Newtonsoft.Json.Linq.JValue(bucket)
    sourceObj("key") = New Newtonsoft.Json.Linq.JValue(key)
    If pages.HasValue Then
        sourceObj("page_count") = New Newtonsoft.Json.Linq.JValue(pages.Value)
    Else
        sourceObj("page_count") = JNull()
    End If
    root("source") = sourceObj

    Dim extraction As New Newtonsoft.Json.Linq.JObject()
    extraction("authorization_status") = JStr(authStatus)
    extraction("authorization_date_received") = JStr(authDateReceived)
    extraction("start_date") = JStr(authStartDate)
    extraction("end_date") = JStr(authEndDate)

    Dim payorObj As New Newtonsoft.Json.Linq.JObject()
    payorObj("name") = JStr(payorName)
    payorObj("program") = JStr(
        If(pocHtml.IndexOf("Community Care Program", StringComparison.OrdinalIgnoreCase) >= 0,
           "Community Care Program", Nothing))
    payorObj("payor_id") = JNull()
    extraction("payor") = payorObj

    Dim careCoord As New Newtonsoft.Json.Linq.JObject()
    careCoord("unit_name") = JStr(assessmentCcu)
    careCoord("contract_number") = JStr(ccuContract)
    careCoord("coordinator_name") = JStr(careCoordinator)
    extraction("care_coordination") = careCoord

    Dim assessObj As New Newtonsoft.Json.Linq.JObject()
    assessObj("date") = JStr(assessmentDate)
    assessObj("type") = JStr(assessType)
    assessObj("id") = JStr(assessmentRecordId)
    assessObj("referral_type") = JNull()
    extraction("assessment") = assessObj

    Dim eligObj As New Newtonsoft.Json.Linq.JObject()
    eligObj("finding") = JStr(eligibilityFinding)
    eligObj("reason") = JStr(eligibilityReason)
    extraction("eligibility") = eligObj

    extraction("special_instructions") = JStr(specialInstructions)

    Dim partInfo As New Newtonsoft.Json.Linq.JObject()
    partInfo("participant_id") = JStr(participantId)
    partInfo("participant_idoa_id") = JStr(idoaId)
    partInfo("participant_name") = JStr(participantName)

    Dim addrObj As New Newtonsoft.Json.Linq.JObject()
    addrObj("full") = JStr(addressFull)
    addrObj("line_1") = JStr(address1)
    addrObj("line_2") = JStr(address2)
    addrObj("city") = JStr(city)
    addrObj("state") = JStr(stateCode)
    addrObj("zip") = JStr(zip)
    addrObj("county") = JNull()
    partInfo("participant_address") = addrObj

    partInfo("participant_gender") = JStr(gender)
    partInfo("participant_dob") = JStr(dob)
    partInfo("participant_ssn") = JStr(ssnMasked)
    partInfo("participant_rin") = JStr(rin)
    partInfo("participant_phones") = ParsePhoneList(participantPhoneRaw)
    partInfo("participant_primary_language") = JStr(primaryLanguage)
    partInfo("participant_emergency_contact") = emergencyArr
    partInfo("participant_authorized_representative") = authRep
    extraction("participant_info") = partInfo

    extraction("signatures") = signaturesArr
    extraction("service_authorizations_summary") = servicesArr
    extraction("attending_provider") = JStr(attending)
    extraction("referring_provider") = JStr(referring)
    extraction("authorization_note") = JStr(authNote)
    extraction("primary_icd_10_code") = JStr(primaryIcdCode)
    extraction("primary_icd_10_description") = JStr(primaryIcdDesc)
    extraction("secondary_dx_1_code") = JStr(sec1Code)
    extraction("secondary_dx_1_description") = JStr(sec1Desc)
    extraction("secondary_dx_2_code") = JStr(sec2Code)
    extraction("secondary_dx_2_description") = JStr(sec2Desc)
    extraction("secondary_dx_3_code") = JStr(sec3Code)
    extraction("secondary_dx_3_description") = JStr(sec3Desc)
    root("extraction") = extraction

    Dim processing As New Newtonsoft.Json.Linq.JObject()
    processing("input_type") = New Newtonsoft.Json.Linq.JValue("pdf")
    processing("market") = New Newtonsoft.Json.Linq.JValue(mkt)
    processing("date") = New Newtonsoft.Json.Linq.JValue(procDate)
    processing("category") = New Newtonsoft.Json.Linq.JValue(cat)
    processing("processed_at") = New Newtonsoft.Json.Linq.JValue(
        System.DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
    processing("pipeline_version") = JStr(pipeVer)
    processing("prompt_version") = JStr(promptVer)
    processing("model_id") = New Newtonsoft.Json.Linq.JValue(model)
    processing("input_tokens") = JNull()
    processing("output_tokens") = JNull()

    processing("cost") = JNull()
    root("processing") = processing

    outputJson = root.ToString(Newtonsoft.Json.Formatting.Indented)
    Console.WriteLine("Built v2 JSON (" & outputJson.Length.ToString() & " chars).")
    Console.WriteLine("Services: " & servicesArr.Count.ToString() &
                      "; Signatures: " & signaturesArr.Count.ToString())

    Dim outPath As String = If(outputJsonPath, "").Trim()
    If Not String.IsNullOrWhiteSpace(outPath) Then
        outPath = System.IO.Path.GetFullPath(outPath)
        Dim dir As String = System.IO.Path.GetDirectoryName(outPath)
        If Not String.IsNullOrWhiteSpace(dir) AndAlso
           Not System.IO.Directory.Exists(dir) Then
            System.IO.Directory.CreateDirectory(dir)
        End If
        System.IO.File.WriteAllText(outPath, outputJson, System.Text.Encoding.UTF8)
        Console.WriteLine("Wrote: " & outPath)
    End If

    success = True
    Console.WriteLine("=== AgingCares Authorization JSON DONE ===")

Catch ex As System.Exception
    errorMessage = ex.Message
    Console.WriteLine("ERROR: " & ex.ToString())
    Throw
End Try
