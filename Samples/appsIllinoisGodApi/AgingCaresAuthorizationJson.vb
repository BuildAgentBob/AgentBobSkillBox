' Builds client-authorization v2 JSON from archived AgingCares HTML paths.
' Prefers ViewPlanOfCare + ViewProviderParticipantHub; other HTMLs ignored for
' extraction. Keeps every v2 key; unknown values are null (lists []).
'
' In:  htmlPaths (String())
' Out: outputJson, errorMessage

Dim success As Boolean = False

Try
    errorMessage = ""
    outputJson = ""

    Console.WriteLine("=== AgingCares Authorization JSON (v2) ===")

    If htmlPaths Is Nothing OrElse htmlPaths.Length = 0 Then
        Throw New System.Exception("htmlPaths is required (array of local HTML file paths).")
    End If

    ' Local defaults — edit here (not UiPath args)
    Dim outputJsonPath As String = Nothing
    Dim sourceBucket As String = "helpathome-us-dev-data-raw"
    Dim sourceKey As String = Nothing
    Dim documentHash As String = Nothing
    Dim pageCount As Integer = 0
    Dim market As String = "IL"
    Dim processingDate As String = System.DateTime.UtcNow.ToString("yyyy-MM-dd")
    Dim category As String = "authorization"
    Dim pipelineVersion As String = "0.0.0"
    Dim promptVersion As String = Nothing
    Dim modelId As String = "uipath-agingcares"

    Dim bucket As String = If(sourceBucket, "").Trim()
    Dim key As String = If(sourceKey, "").Trim()
    If String.IsNullOrWhiteSpace(key) Then
        key = "pdf-extraction/raw/market=" & market &
              "/date=" & processingDate &
              "/category=" & category &
              "/document.pdf"
    End If

    Dim mkt As String = If(String.IsNullOrWhiteSpace(market), "IL", market.Trim().ToUpperInvariant())
    Dim cat As String = If(String.IsNullOrWhiteSpace(category), "authorization", category.Trim())
    Dim procDate As String = If(processingDate, "").Trim()
    If String.IsNullOrWhiteSpace(procDate) Then
        procDate = System.DateTime.UtcNow.ToString("yyyy-MM-dd")
    End If

    Dim pipeVer As String = If(pipelineVersion, "").Trim()
    If String.IsNullOrWhiteSpace(pipeVer) Then pipeVer = "0.0.0"
    Dim promptVer As String = If(promptVersion, "").Trim()
    If String.IsNullOrWhiteSpace(promptVer) Then promptVer = Nothing
    Dim model As String = If(modelId, "").Trim()
    If String.IsNullOrWhiteSpace(model) Then model = "uipath-agingcares"
    Dim docHash As String = If(documentHash, "").Trim()
    If String.IsNullOrWhiteSpace(docHash) Then docHash = Nothing
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
                phone("NUMBER") = New Newtonsoft.Json.Linq.JValue(num)
                If label Is Nothing Then
                    phone("LABEL") = Newtonsoft.Json.Linq.JValue.CreateNull()
                Else
                    phone("LABEL") = New Newtonsoft.Json.Linq.JValue(label)
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
            f("HOURS_PER_DAY") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("HOURS_PER_WEEK") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("HOURS_PER_MONTH") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("VISITS_PER_DAY") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("VISITS_PER_WEEK") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("VISITS_PER_MONTH") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("UNITS_PER_DAY") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("UNITS_PER_WEEK") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("UNITS_PER_MONTH") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("INSTALLATION_REQUIRED") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("CONNECTIVITY_TYPE") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("SHARED_STATUS") = Newtonsoft.Json.Linq.JValue.CreateNull()
            f("DAY_OF_WEEK") = New Newtonsoft.Json.Linq.JArray()
            f("OTHER_NOTES") = Newtonsoft.Json.Linq.JValue.CreateNull()
            Return f
        End Function

    ' ---------- load HTMLs ----------
    Dim pocHtml As String = Nothing
    Dim hubHtml As String = Nothing
    Dim pocPath As String = Nothing
    Dim hubPath As String = Nothing

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
            Console.WriteLine("SKIP (not Hub/POC): " & p)
        End If
    Next

    If pocHtml Is Nothing Then
        Throw New System.Exception(
            "No ViewPlanOfCare HTML found in htmlPaths. Archive Plan of Care first.")
    End If

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
                ec("NAME") = em.Groups(1).Value.Trim()
                ec("RELATIONSHIP") = em.Groups(2).Value.Trim()
                ec("PHONES") = ParsePhoneList(em.Groups(3).Value)
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

    If String.IsNullOrWhiteSpace(assessmentCcu) AndAlso
       Not String.IsNullOrWhiteSpace(hubCcuName) Then
        assessmentCcu = hubCcuName
    End If

    ' ---------- Services ----------
    Dim servicesArr As New Newtonsoft.Json.Linq.JArray()
    Dim signaturesArr As New Newtonsoft.Json.Linq.JArray()

    ' Participant / AR signature
    Dim partSigPresent As Boolean =
        Not String.IsNullOrWhiteSpace(participantSig) AndAlso
        participantSig.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
    Dim partSigObj As New Newtonsoft.Json.Linq.JObject()
    If Not String.IsNullOrWhiteSpace(assistingPerson) Then
        partSigObj("SIGNER_ROLE") = New Newtonsoft.Json.Linq.JValue("AUTHORIZED_REPRESENTATIVE")
        partSigObj("SIGNER_NAME") = JStr(assistingPerson)
    Else
        partSigObj("SIGNER_ROLE") = New Newtonsoft.Json.Linq.JValue("PARTICIPANT")
        partSigObj("SIGNER_NAME") = JStr(participantName)
    End If
    partSigObj("SIGNATURE_PRESENT") = New Newtonsoft.Json.Linq.JValue(partSigPresent)
    If partSigPresent Then
        partSigObj("SIGNATURE_DATE") = JStr(ParseDateYmd(participantSigDate))
    Else
        partSigObj("SIGNATURE_DATE") = JNull()
    End If
    partSigObj("PROVIDER_NAME") = JNull()
    partSigObj("SERVICE_TYPE") = JNull()
    partSigObj("SERVICE_LINE_NUMBER") = JNull()
    signaturesArr.Add(partSigObj)

    ' Care coordinator signature
    Dim ccuSigPresent As Boolean =
        Not String.IsNullOrWhiteSpace(ccuSig) AndAlso
        ccuSig.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
    Dim ccSigObj As New Newtonsoft.Json.Linq.JObject()
    ccSigObj("SIGNER_ROLE") = New Newtonsoft.Json.Linq.JValue("CARE_COORDINATOR")
    ccSigObj("SIGNER_NAME") = JStr(careCoordinator)
    ccSigObj("SIGNATURE_PRESENT") = New Newtonsoft.Json.Linq.JValue(ccuSigPresent)
    If ccuSigPresent Then
        ccSigObj("SIGNATURE_DATE") = JStr(ParseDateYmd(ccuSigDate))
    Else
        ccSigObj("SIGNATURE_DATE") = JNull()
    End If
    ccSigObj("PROVIDER_NAME") = JNull()
    ccSigObj("SERVICE_TYPE") = JNull()
    ccSigObj("SERVICE_LINE_NUMBER") = JNull()
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
        freq("HOURS_PER_DAY") = JNum(ParseNumber(hoursDay))
        freq("HOURS_PER_WEEK") = JNum(ParseNumber(hoursWeek))
        freq("VISITS_PER_WEEK") = JNum(ParseNumber(timesWeek))

        ' EHRS / device fields
        Dim isDevice As Boolean =
            svcType.IndexOf("Emergency", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            svcType.IndexOf("Response", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
            svcType.IndexOf("Medication", StringComparison.OrdinalIgnoreCase) >= 0

        If isDevice OrElse Not String.IsNullOrWhiteSpace(monthlyShares) Then
            Dim shares As String = If(monthlyShares, "")
            If Not String.Equals(shares, "N", StringComparison.OrdinalIgnoreCase) AndAlso
               shares.Length > 0 Then
                freq("VISITS_PER_MONTH") = JNum(ParseNumber(shares))
                    If shares.IndexOf("Unshared", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    freq("SHARED_STATUS") = New Newtonsoft.Json.Linq.JValue("UNSHARED")
                ElseIf shares.IndexOf("Shared", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    freq("SHARED_STATUS") = New Newtonsoft.Json.Linq.JValue("SHARED")
                End If
            End If
        End If

        If isDevice Then
            freq("INSTALLATION_REQUIRED") = JBool(YnToBool(installation))
            If Not String.IsNullOrWhiteSpace(connectionType) AndAlso
               Not String.Equals(connectionType, "N", StringComparison.OrdinalIgnoreCase) Then
                freq("CONNECTIVITY_TYPE") = New Newtonsoft.Json.Linq.JValue(connectionType)
            End If
        End If

        Dim lineNum As Integer = i + 1
        Dim svcObj As New Newtonsoft.Json.Linq.JObject()
        svcObj("LINE_NUMBER") = New Newtonsoft.Json.Linq.JValue(lineNum)
        svcObj("SERVICE_TYPE") = New Newtonsoft.Json.Linq.JValue(svcType)
        svcObj("SERVICE_ACTION") = JNull()
        svcObj("PROVIDER_NAME") = JStr(providerName)
        svcObj("PROVIDER_LOCATION") = JNull()
        svcObj("PROVIDER_PHONES") = ParsePhoneList(providerPhone)
        svcObj("REFERRAL_DATE") = JStr(referralDate)
        svcObj("SERVICE_START_DATE") = JStr(svcStart)
        svcObj("SERVICE_END_DATE") = JNull()
        svcObj("FREQUENCY") = freq
        servicesArr.Add(svcObj)

        ' Provider signature block
        Dim provPresent As Boolean =
            Not String.IsNullOrWhiteSpace(provSig) AndAlso
            provSig.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        Dim provSignerName As String = Nothing
        Dim sigObj As New Newtonsoft.Json.Linq.JObject()
        sigObj("SIGNER_ROLE") = New Newtonsoft.Json.Linq.JValue("PROVIDER")
        sigObj("SIGNER_NAME") = JStr(provSignerName)
        sigObj("SIGNATURE_PRESENT") = New Newtonsoft.Json.Linq.JValue(provPresent)
        If provPresent Then
            sigObj("SIGNATURE_DATE") = JStr(ParseDateYmd(provSigDate))
        Else
            sigObj("SIGNATURE_DATE") = JNull()
        End If
        sigObj("PROVIDER_NAME") = JStr(providerName)
        sigObj("SERVICE_TYPE") = New Newtonsoft.Json.Linq.JValue(svcType)
        sigObj("SERVICE_LINE_NUMBER") = New Newtonsoft.Json.Linq.JValue(lineNum)
        signaturesArr.Add(sigObj)
    Next

    If servicesArr.Count = 0 Then
        Throw New System.Exception("No SERVICE_AUTHORIZATIONS found in Plan of Care HTML.")
    End If

    ' Provider SIGNER_NAME is printed after "Authorized Signature/Date" under the service type
    For Each sigTok As Newtonsoft.Json.Linq.JToken In signaturesArr
        Dim sigObj As Newtonsoft.Json.Linq.JObject =
            CType(sigTok, Newtonsoft.Json.Linq.JObject)
        If Not String.Equals(CStr(sigObj("SIGNER_ROLE")), "PROVIDER") Then Continue For
        If sigObj("SIGNER_NAME").Type <> Newtonsoft.Json.Linq.JTokenType.Null Then Continue For
        Dim st As String = CStr(sigObj("SERVICE_TYPE"))
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
                    sigObj("SIGNER_NAME") = cand
                End If
            End If
        Next
    Next

    ' ATTENDING / REFERRING
    Dim attending As String = Nothing
    If servicesArr.Count > 0 Then
        Dim firstProv As Newtonsoft.Json.Linq.JToken = servicesArr(0)("PROVIDER_NAME")
        If firstProv IsNot Nothing AndAlso firstProv.Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
            attending = CStr(firstProv)
        End If
    End If
    Dim referring As String = careCoordinator

    ' AUTHORIZATION_NOTE — pipeline summary only from structured printed pieces
    Dim noteParts As New System.Collections.Generic.List(Of String)()
    If authDateReceived IsNot Nothing Then
        noteParts.Add("Eligibility notification " & authDateReceived)
    End If
    If authStatus IsNot Nothing Then noteParts.Add("Status " & authStatus)
    If authAction IsNot Nothing Then noteParts.Add("Action " & authAction)
    For Each svcTok As Newtonsoft.Json.Linq.JToken In servicesArr
        Dim so As Newtonsoft.Json.Linq.JObject = CType(svcTok, Newtonsoft.Json.Linq.JObject)
        Dim bit As String = CStr(so("SERVICE_TYPE"))
        Dim f As Newtonsoft.Json.Linq.JObject = CType(so("FREQUENCY"), Newtonsoft.Json.Linq.JObject)
        If f("HOURS_PER_WEEK").Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
            bit &= ": " & f("HOURS_PER_WEEK").ToString() & " hrs/week"
        ElseIf f("VISITS_PER_MONTH").Type <> Newtonsoft.Json.Linq.JTokenType.Null Then
            bit &= ": " & f("VISITS_PER_MONTH").ToString() & " monthly"
        End If
        noteParts.Add(bit)
    Next
    Dim authNote As String =
        If(noteParts.Count > 0, String.Join("; ", noteParts), Nothing)

    ' Authorized representative object
    Dim authRep As Newtonsoft.Json.Linq.JToken = Newtonsoft.Json.Linq.JValue.CreateNull()
    If Not String.IsNullOrWhiteSpace(assistingPerson) Then
        Dim ar As New Newtonsoft.Json.Linq.JObject()
        ar("NAME") = assistingPerson
        ar("RELATIONSHIP") = JStr(assistingRel)
        ar("PHONES") = New Newtonsoft.Json.Linq.JArray()
        authRep = ar
    End If

    Dim coordinatorPhones As Newtonsoft.Json.Linq.JArray = ParsePhoneList(ccuPhone)

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
    root("SCHEMA_VERSION") = New Newtonsoft.Json.Linq.JValue("2.0")

    Dim sourceObj As New Newtonsoft.Json.Linq.JObject()
    sourceObj("BUCKET") = New Newtonsoft.Json.Linq.JValue(bucket)
    sourceObj("KEY") = New Newtonsoft.Json.Linq.JValue(key)
    sourceObj("DOCUMENT_HASH") = JStr(docHash)
    If pages.HasValue Then
        sourceObj("PAGE_COUNT") = New Newtonsoft.Json.Linq.JValue(pages.Value)
    Else
        sourceObj("PAGE_COUNT") = JNull()
    End If
    root("source") = sourceObj

    Dim extraction As New Newtonsoft.Json.Linq.JObject()
    extraction("AUTHORIZATION_NUMBER") = JNull()
    extraction("AUTHORIZATION_STATUS") = JStr(authStatus)
    extraction("AUTHORIZATION_ACTION") = JStr(authAction)
    extraction("AUTHORIZATION_DATE_RECEIVED") = JStr(authDateReceived)
    extraction("START_DATE") = JNull()
    extraction("END_DATE") = JNull()
    extraction("VISIT_TYPE") = JNull()
    extraction("IMPORT_EXTERNAL_ID") = JNull()

    Dim payorObj As New Newtonsoft.Json.Linq.JObject()
    payorObj("NAME") = JStr(payorName)
    payorObj("PROGRAM") = JStr(
        If(pocHtml.IndexOf("Community Care Program", StringComparison.OrdinalIgnoreCase) >= 0,
           "Community Care Program", Nothing))
    payorObj("PAYOR_ID") = JNull()
    extraction("PAYOR") = payorObj

    Dim careCoord As New Newtonsoft.Json.Linq.JObject()
    careCoord("UNIT_NAME") = JStr(assessmentCcu)
    careCoord("CONTRACT_NUMBER") = JStr(ccuContract)
    Dim coordObj As New Newtonsoft.Json.Linq.JObject()
    coordObj("NAME") = JStr(careCoordinator)
    coordObj("PHONES") = coordinatorPhones
    coordObj("EMAIL") = JNull()
    careCoord("COORDINATOR") = coordObj
    extraction("CARE_COORDINATION") = careCoord

    Dim assessObj As New Newtonsoft.Json.Linq.JObject()
    assessObj("DATE") = JStr(assessmentDate)
    assessObj("TYPE") = JStr(assessType)
    extraction("ASSESSMENT") = assessObj

    Dim eligObj As New Newtonsoft.Json.Linq.JObject()
    eligObj("FINDING") = JStr(eligibilityFinding)
    eligObj("REASON") = JStr(eligibilityReason)
    extraction("ELIGIBILITY") = eligObj

    extraction("SPECIAL_INSTRUCTIONS") = JStr(specialInstructions)
    extraction("TOTAL_PLAN_COST") = JNull()

    Dim partInfo As New Newtonsoft.Json.Linq.JObject()
    partInfo("PARTICIPANT_ID") = JStr(participantId)
    partInfo("PARTICIPANT_IDOA_ID") = JStr(idoaId)
    partInfo("PARTICIPANT_NAME") = JStr(participantName)

    Dim addrObj As New Newtonsoft.Json.Linq.JObject()
    addrObj("FULL") = JStr(addressFull)
    addrObj("LINE_1") = JStr(address1)
    addrObj("LINE_2") = JStr(address2)
    addrObj("CITY") = JStr(city)
    addrObj("STATE") = JStr(stateCode)
    addrObj("ZIP") = JStr(zip)
    addrObj("COUNTY") = JNull()
    partInfo("PARTICIPANT_ADDRESS") = addrObj

    partInfo("PARTICIPANT_GENDER") = JStr(gender)
    partInfo("PARTICIPANT_DOB") = JStr(dob)
    partInfo("PARTICIPANT_SSN") = JStr(ssnMasked)
    partInfo("PARTICIPANT_RIN") = JStr(rin)
    partInfo("PARTICIPANT_PAYER_MEMBER_ID") = JNull()
    partInfo("PARTICIPANT_MEDICARE_ID") = JNull()
    partInfo("PARTICIPANT_PHONES") = ParsePhoneList(participantPhoneRaw)
    partInfo("PARTICIPANT_PRIMARY_LANGUAGE") = JStr(primaryLanguage)
    partInfo("PARTICIPANT_INTERPRETER_NEEDED") = JNull()
    partInfo("PARTICIPANT_EMERGENCY_CONTACT") = emergencyArr
    partInfo("PARTICIPANT_AUTHORIZED_REPRESENTATIVE") = authRep
    extraction("PARTICIPANT_INFO") = partInfo

    extraction("SIGNATURES") = signaturesArr
    extraction("SERVICE_AUTHORIZATIONS") = servicesArr
    extraction("DIAGNOSES") = New Newtonsoft.Json.Linq.JArray()
    extraction("ATTENDING_PROVIDER") = JStr(attending)
    extraction("REFERRING_PROVIDER") = JStr(referring)
    extraction("AUTHORIZATION_NOTE") = JStr(authNote)
    root("extraction") = extraction

    Dim processing As New Newtonsoft.Json.Linq.JObject()
    processing("INPUT_TYPE") = New Newtonsoft.Json.Linq.JValue("pdf")
    processing("MARKET") = New Newtonsoft.Json.Linq.JValue(mkt)
    processing("DATE") = New Newtonsoft.Json.Linq.JValue(procDate)
    processing("CATEGORY") = New Newtonsoft.Json.Linq.JValue(cat)
    processing("PROCESSED_AT") = New Newtonsoft.Json.Linq.JValue(
        System.DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
    processing("PIPELINE_VERSION") = New Newtonsoft.Json.Linq.JValue(pipeVer)
    processing("PROMPT_VERSION") = JStr(promptVer)
    processing("MODEL_ID") = New Newtonsoft.Json.Linq.JValue(model)
    processing("INPUT_TOKENS") = New Newtonsoft.Json.Linq.JValue(0)
    processing("OUTPUT_TOKENS") = New Newtonsoft.Json.Linq.JValue(0)

    Dim costObj As New Newtonsoft.Json.Linq.JObject()
    costObj("CURRENCY") = New Newtonsoft.Json.Linq.JValue("USD")
    costObj("INPUT_COST") = New Newtonsoft.Json.Linq.JValue(0)
    costObj("OUTPUT_COST") = New Newtonsoft.Json.Linq.JValue(0)
    costObj("TOTAL_COST") = New Newtonsoft.Json.Linq.JValue(0)
    processing("COST") = costObj

    Dim reviewObj As New Newtonsoft.Json.Linq.JObject()
    reviewObj("STATUS") = New Newtonsoft.Json.Linq.JValue("NOT_REVIEWED")
    reviewObj("REVIEWED_BY") = JNull()
    reviewObj("REVIEWED_AT") = JNull()
    processing("REVIEW") = reviewObj

    processing("FIELD_CONFIDENCE") = JNull()
    processing("VALIDATION") = JNull()
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
