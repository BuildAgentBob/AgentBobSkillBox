' Converts a local self-contained HTML file (e.g. ViewPlanOfCare archive) to PDF
' using installed Chrome or Edge headless --print-to-pdf (no visible browser).
' No CSS injection — archive HTML already includes agingcares-print-fix from ArchivePage.
' inputHtmlPath + outputPdfPath required; browserPath optional override.
' errorMessage Out. Throws on failure (caught → errorMessage).

Dim exitCode As Integer = -1
Dim browserUsed As String = ""
Dim success As Boolean = False

Try
    errorMessage = ""

    Console.WriteLine("=== AgingCares HTML → PDF (headless Chrome/Edge) ===")

    Dim htmlPath As String = If(inputHtmlPath, "").Trim()
    Dim pdfPath As String = If(outputPdfPath, "").Trim()
    Dim browserOverride As String = If(browserPath, "").Trim()

    If String.IsNullOrWhiteSpace(htmlPath) Then
        Console.WriteLine("ABORT: inputHtmlPath is missing.")
        Throw New System.Exception("inputHtmlPath is required.")
    End If
    If Not System.IO.File.Exists(htmlPath) Then
        Console.WriteLine("ABORT: input HTML file not found.")
        Throw New System.Exception("input HTML not found: " & htmlPath)
    End If

    If String.IsNullOrWhiteSpace(pdfPath) Then
        Console.WriteLine("ABORT: outputPdfPath is missing.")
        Throw New System.Exception("outputPdfPath is required.")
    End If

    ' Resolve full paths
    htmlPath = System.IO.Path.GetFullPath(htmlPath)
    pdfPath = System.IO.Path.GetFullPath(pdfPath)

    Dim outDir As String = System.IO.Path.GetDirectoryName(pdfPath)
    If Not String.IsNullOrWhiteSpace(outDir) AndAlso
       Not System.IO.Directory.Exists(outDir) Then
        System.IO.Directory.CreateDirectory(outDir)
        Console.WriteLine("Created output folder.")
    End If

    If System.IO.File.Exists(pdfPath) Then
        System.IO.File.Delete(pdfPath)
        Console.WriteLine("Removed existing PDF at output path.")
    End If

    Console.WriteLine(
        "Inputs OK | htmlBytes=" &
        New System.IO.FileInfo(htmlPath).Length.ToString() &
        " | hasBrowserOverride=" &
        (Not String.IsNullOrWhiteSpace(browserOverride)).ToString() &
        " | printCss=from archive"
    )


    Dim FindBrowser As Func(Of String) =
        Function() As String
            If Not String.IsNullOrWhiteSpace(browserOverride) Then
                If System.IO.File.Exists(browserOverride) Then
                    Return browserOverride
                End If
                Throw New System.Exception(
                    "browserPath not found: " & browserOverride
                )
            End If

            Dim candidates As New System.Collections.Generic.List(Of String)()

            Dim pf As String =
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.ProgramFiles
                )
            Dim pf86 As String =
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.ProgramFilesX86
                )
            Dim localApp As String =
                System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.LocalApplicationData
                )

            candidates.Add(
                System.IO.Path.Combine(
                    pf, "Google", "Chrome", "Application", "chrome.exe"
                )
            )
            candidates.Add(
                System.IO.Path.Combine(
                    pf86, "Google", "Chrome", "Application", "chrome.exe"
                )
            )
            candidates.Add(
                System.IO.Path.Combine(
                    localApp, "Google", "Chrome", "Application", "chrome.exe"
                )
            )
            candidates.Add(
                System.IO.Path.Combine(
                    pf, "Microsoft", "Edge", "Application", "msedge.exe"
                )
            )
            candidates.Add(
                System.IO.Path.Combine(
                    pf86, "Microsoft", "Edge", "Application", "msedge.exe"
                )
            )
            candidates.Add(
                System.IO.Path.Combine(
                    localApp, "Microsoft", "Edge", "Application", "msedge.exe"
                )
            )

            For Each c As String In candidates
                If System.IO.File.Exists(c) Then
                    Return c
                End If
            Next

            Return ""
        End Function


    browserUsed = FindBrowser()
    If String.IsNullOrWhiteSpace(browserUsed) Then
        Console.WriteLine("ABORT: Chrome/Edge executable not found.")
        Throw New System.Exception(
            "Chrome or Edge not found. Install one, or set browserPath to chrome.exe / msedge.exe."
        )
    End If

    Console.WriteLine(
        "Using browser | name=" &
        System.IO.Path.GetFileName(browserUsed)
    )


    Dim fileUri As String =
        New System.Uri(htmlPath).AbsoluteUri

    Dim Quote As Func(Of String, String) =
        Function(p As String) As String
            If p Is Nothing Then
                Return """"""
            End If
            If p.StartsWith("""") AndAlso p.EndsWith("""") Then
                Return p
            End If
            Return """" & p & """"
        End Function

    Dim args As String =
        "--headless=new " &
        "--disable-gpu " &
        "--no-first-run " &
        "--no-default-browser-check " &
        "--allow-file-access-from-files " &
        "--hide-scrollbars " &
        "--run-all-compositor-stages-before-draw " &
        "--virtual-time-budget=8000 " &
        "--no-pdf-header-footer " &
        "--print-to-pdf=" & Quote(pdfPath) & " " &
        Quote(fileUri)

    Console.WriteLine("Starting headless print-to-pdf (uses @page from archive HTML)...")

    Dim psi As New System.Diagnostics.ProcessStartInfo()
    psi.FileName = browserUsed
    psi.Arguments = args
    psi.UseShellExecute = False
    psi.CreateNoWindow = True
    psi.RedirectStandardOutput = True
    psi.RedirectStandardError = True
    psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden

    Dim stdOut As String = ""
    Dim stdErr As String = ""

    Using proc As New System.Diagnostics.Process()
        proc.StartInfo = psi
        proc.Start()

        stdOut = proc.StandardOutput.ReadToEnd()
        stdErr = proc.StandardError.ReadToEnd()

        Dim finished As Boolean = proc.WaitForExit(180000)
        If Not finished Then
            Try
                proc.Kill()
            Catch
            End Try
            Console.WriteLine("ABORT: browser print timed out after 180s.")
            Throw New System.Exception(
                "Headless print-to-pdf timed out after 180 seconds."
            )
        End If

        exitCode = proc.ExitCode
    End Using

    Console.WriteLine(
        "Browser exited | exitCode=" & exitCode.ToString()
    )

    If Not System.IO.File.Exists(pdfPath) Then
        Console.WriteLine("ABORT: PDF file was not created.")
        Dim hint As String = ""
        If Not String.IsNullOrWhiteSpace(stdErr) Then
            hint = " stderrChars=" & stdErr.Length.ToString()
        End If
        Throw New System.Exception(
            "PDF was not created at outputPdfPath. exitCode=" &
            exitCode.ToString() & hint
        )
    End If

    Dim pdfInfo As New System.IO.FileInfo(pdfPath)
    If pdfInfo.Length < 100 Then
        Console.WriteLine("ABORT: PDF file is too small / likely empty.")
        Throw New System.Exception(
            "PDF looks empty. fileBytes=" & pdfInfo.Length.ToString()
        )
    End If

    success = True
    Console.WriteLine(
        "=== HTML → PDF completed successfully | fileBytes=" &
        pdfInfo.Length.ToString() & " ==="
    )

Catch ex As Exception

    success = False
    errorMessage = ex.ToString()
    Console.WriteLine("=== HTML → PDF FAILED ===")
    Console.WriteLine(
        "At failure: exitCode=" & exitCode.ToString() &
        " | browser=" &
        If(String.IsNullOrWhiteSpace(browserUsed), "(none)",
           System.IO.Path.GetFileName(browserUsed))
    )
    Console.WriteLine(errorMessage)

End Try
