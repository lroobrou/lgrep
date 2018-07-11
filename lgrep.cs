// porttowin version
// dir /b /s "%windir%\microsoft.net\*csc.exe"
// C:\Windows\microsoft.net\Framework\v2.0.50727\csc.exe lgrep.cs /d:EXTERNALCODE /o
// C:\Windows\microsoft.net\Framework\v2.0.50727\csc.exe lgrep.cs /d:EXTERNALCODE /DEBUG /define:DEBUG;TRACE
// notepad %OM_SERVER%\bin\lgrep.cs
// pushd %OM_SERVER%\bin && C:\Windows\microsoft.net\Framework\v2.0.50727\csc.exe lgrep.cs /d:EXTERNALCODE /o && del lgrep.cs && popd
#define EXTERNALCODE

using System;
using System.IO;
using System.Globalization;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.CodeDom.Compiler;
using Microsoft.CSharp;
using Microsoft.VisualBasic;
using System.Reflection;
using System.Diagnostics; //stopwatch
using System.Security;
using System.Security.Permissions;
using System.Linq;

// XML
using System.Xml;
using System.Xml.XPath;
using System.Runtime.Remoting; // Used for XML invalid character filtering

// Detect if output is piped to lgrep
using System.Runtime.InteropServices;

using System.Threading;

public static class ConsoleEx {
    private static object l = new object();
    private static Thread t;
    private static string _statusline;
    private static bool _statusline_drawn = false;
    private static bool _draw_statusline = false;
    private static int _statusline_length = 0;
    public static ConsoleSpinner spinner = new ConsoleSpinner("slashes", 130);
    private static bool keepThrobbing = true;

    static ConsoleEx() {
        t = new Thread(ShowThrobber);
        t.Start();
    }

#if DOTNET2
    // May throw System.Security.SecurityException: System.Security.Permissions.SecurityPermission
    // Not all systems allow to run unmanaged code.
    public static bool IsOutputRedirected {
        get { return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stdout)); }
    }

    public static bool IsInputRedirected {
        get { return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stdin)); }
    }

    // P/Invoke:
    //private enum FileType { Unknown, Disk, Char, Pipe };
    //private enum StdHandle { Stdin = -10, Stdout = -11, Stderr = -12 };
    [DllImport("kernel32.dll")]
        private static extern FileType GetFileType(IntPtr hdl);
    [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(StdHandle std);
#else
    private enum FileType { Unknown, Disk, Char, Pipe };
    private enum StdHandle { Stdin = -10, Stdout = -11, Stderr = -12 };
    [DllImport("kernel32.dll")]
        private static extern FileType GetFileType(IntPtr hdl);
    [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(StdHandle std);

    public static bool IsOutputRedirected {
        //get { return IsConsoleSizeZero && !Console.KeyAvailable; }
        // TODO: The system.missingmethodexception is sometimes thrown, but this try catch
        // doesn't seem to catch that ? 
        get {
            try {
                return Console.IsOutputRedirected;
            } catch (System.MissingMethodException) { // older .NET versions
                return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stdin)); 
            }
        }
    }

    public static bool IsInputRedirected {
        //get { return IsConsoleSizeZero && Console.KeyAvailable; }
        get { 
            try {
                return Console.IsInputRedirected;
            } catch (System.MissingMethodException) { // older .NET versions
                return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stdout)); 
            }
        }
    }

    private static bool IsConsoleSizeZero {
        get {
            try {
                return (0 == (Console.WindowHeight + Console.WindowWidth));
            }
            catch (Exception) {
                return true;
            }
        }
    }
#endif

    public static void WriteErrorLine(string line, params object[] args) {
        lock(l) {
            if (_statusline_drawn)
                _FlushStatusLine();

            Console.Error.WriteLine(line, args);
            _WriteStatusLine();
        }
    }

    public static void Write(string line, params object[] args) {
        if (grepper.showProgress) {
            lock(l) {
                Console.Write(line, args);
            }
        }
        else {
            Console.Write(line, args);
        }
    }

    public static void WriteLine(string line, params object[] args) {
        if (grepper.showProgress) {
            lock(l) {
                if (_statusline_drawn)
                    _FlushStatusLine();

                Console.WriteLine(line, args);
                _WriteStatusLine();
            }
        }
        else {
            Console.WriteLine(line, args);
        }
    }

    public static bool IsLinux
    {
        get
        {
            int p = (int) Environment.OSVersion.Platform;
            return (p == 4) || (p == 6) || (p == 128);
        }
    }
    
    public static void SetMatchColor() {
        if (IsLinux) {
            Console.Write(AnsiColorCodes.BrightRed);
        } else {
            Console.ForegroundColor = grepper.foregroundColor;
            Console.BackgroundColor = grepper.backgroundColor;
        }
    }

    public static void SetFileColor() {
        if (IsLinux) {
            Console.Write(AnsiColorCodes.BrightGreen);
        } else {
            Console.ForegroundColor = grepper.fileForegroundColor;
            Console.BackgroundColor = grepper.fileBackgroundColor;
        }
    }

    public static void SetControlColor() {
        if (IsLinux) {
            Console.Write(AnsiColorCodes.BrightYellow);
        } else {
            Console.ForegroundColor = grepper.controlForegroundColor;
            Console.BackgroundColor = grepper.controlBackgroundColor;
        }
    }

    public static void ResetColor() {
        if (IsLinux) {
            Console.Write(AnsiColorCodes.Reset);
        } else {
            Console.ResetColor();
        }
    }

    public static void WriteMatchLine(string line, string file, long linenumber) {
        bool showFile = grepper.showFileName && !grepper.showFileNameOnce;

        lock(l) {
            if (grepper.showCount == false) {

                if (grepper.showProgress && Console.BufferWidth > 0)
                    Console.Write("\r".PadRight(Console.BufferWidth-1) + "\r");

                if (grepper.showLineNumbers && showFile) {
                    SetFileColor();
                    Console.Write (Util.FormatFileName(file));
                    ResetColor();

                    Console.Write (" (");

                    SetControlColor();
                    Console.Write (linenumber);
                    ResetColor();
                    Console.Write ("): ");
                }
                else if (grepper.showLineNumbers && !showFile) {
                    SetControlColor();
                    Console.Write (linenumber);
                    ResetColor();

                    Console.Write (": ");
                }
                else if (showFile) {
                    SetFileColor();
                    Console.Write (Util.FormatFileName(file));
                    ResetColor();

                    Console.Write (": ");
                }

                grepper.m.printLineColor (line);

                if (grepper.showProgress && Console.BufferWidth > 0)
                    _WriteStatusLine();
            }
        }
    }

    public static string StatusLine
    {
        set { _statusline = value; }
    }

    private static void _WriteStatusLine() {
        string pstring;
        int maxLength = Console.BufferWidth - 1;
        if (!_draw_statusline) return;

        if (Console.BufferWidth > 0) {
            _statusline_drawn = true;
            pstring = string.Format("\r {0}  {1}", spinner.GetCurrent(), _statusline);

            if (maxLength > 0) {
                _statusline_length = Math.Min(pstring.Length, maxLength);
                Console.Write("\r".PadRight(Console.BufferWidth-1) + "\r");
                Console.Write(pstring.Substring(0, _statusline_length));
            }
        }
    }

    public static void WriteStatusLine() {
        lock(l) {
            _WriteStatusLine();
        }
    }

    private static void _FlushStatusLine() {
        if (Console.BufferWidth > 0) {
            Console.Write("\r".PadRight(Console.BufferWidth-1) + "\r");
        }
        _statusline_drawn = false;
    }

    public static void FlushStatusLine() {
        if (_statusline_drawn) {
            lock(l) {
                _FlushStatusLine();
            }
        }
    }

    public static void ShowThrobber() {
        try {
            Thread.Sleep(250);
            _draw_statusline = true;
            while (keepThrobbing && grepper.showProgress) {
                spinner.Tick();
                WriteStatusLine();
                Thread.Sleep(250);
            }
        }
        catch (ThreadAbortException)
        {
            ConsoleEx.FlushStatusLine();
        }
    }

    public static void CancelThrobber() {
        keepThrobbing = false;
        FlushStatusLine();
        t.Abort();
    }
}


public static class AnsiColorCodes {
    // Foreground
    public const string Black                   = "\u001b[30m";
    public const string Red                     = "\u001b[31m";
    public const string Green                   = "\u001b[32m";
    public const string Yellow                  = "\u001b[33m";
    public const string Blue                    = "\u001b[34m";
    public const string Magenta                 = "\u001b[35m";
    public const string Cyan                    = "\u001b[36m";
    public const string White                   = "\u001b[37m";
    public const string BrightBlack             = "\u001b[30;1m";
    public const string BrightRed               = "\u001b[31;1m";
    public const string BrightGreen             = "\u001b[32;1m";
    public const string BrightYellow            = "\u001b[33;1m";
    public const string BrightBlue              = "\u001b[34;1m";
    public const string BrightMagenta           = "\u001b[35;1m";
    public const string BrightCyan              = "\u001b[36;1m";
    public const string BrightWhite             = "\u001b[37;1m";

    // Background
    public const string BackgroundBlack         = "\u001b[40m";
    public const string BackgroundRed           = "\u001b[41m";
    public const string BackgroundGreen         = "\u001b[42m";
    public const string BackgroundYellow        = "\u001b[43m";
    public const string BackgroundBlue          = "\u001b[44m";
    public const string BackgroundMagenta       = "\u001b[45m";
    public const string BackgroundCyan          = "\u001b[46m";
    public const string BackgroundWhite         = "\u001b[47m";
    public const string BackgroundBrightBlack   = "\u001b[40;1m";
    public const string BackgroundBrightRed     = "\u001b[41;1m";
    public const string BackgroundBrightGreen   = "\u001b[42;1m";
    public const string BackgroundBrightYellow  = "\u001b[43;1m";
    public const string BackgroundBrightBlue    = "\u001b[44;1m";
    public const string BackgroundBrightMagenta = "\u001b[45;1m";
    public const string BackgroundBrightCyan    = "\u001b[46;1m";
    public const string BackgroundBrightWhite   = "\u001b[47;1m";

    // Decorations
    public const string Reset                   = "\u001b[0m";
    public const string Bold                    = "\u001b[1m";
    public const string Underline               = "\u001b[4m";
    public const string Reversed                = "\u001b[7m";
};

public enum SearchMode
{
    begin
        , end
        , all
        , wholeline
        , xpath
        , regex
        , regexUniLine
#if EXTERNALCODE
        , coded
#endif
};

public class FixedSizedQueue<T> : Queue<T>
{
    private int _size;
    public int Size {
        get { return _size; }
        set { _size = value; }
    }

    public FixedSizedQueue(int size)
    {
        _size = size;
    }

    public FixedSizedQueue(long size)
    {
        _size = Convert.ToInt32(size);
    }

    public new void Enqueue(T obj)
    {
        base.Enqueue(obj);
        lock (this)
        {
            while (base.Count > _size)
            {
                base.Dequeue();
            }
        }
    }
}

public class grepper
{
    // search options
    // TODO: options to add:
    //   * /fgcolor=, /bgcolor= to highlight matches
    //   * /ffgcolor=, /fbgcolor= to highlight filenames
    //   /color = on, off, auto
    //   * /lines=start,end to indicate which lines to scan. Negative numbers start from the end of the file
    //   * /F, read list of filenames to scan from a file, or stdin
    //   * /G, read list of search strings to search
    //   * /D, list of directories to scan
    //   /type= search only files of type xxx (from ACK) (add --listtypes)
    //
    //   show the column of the (first) match
    //   * show filename only once (like ack)
    //   show number of matches per file, too (on top of the matches themselves)
    //   give a proper return code when something has been found, errors occured
    //   only scan files older than x (for example)
    //   use IsControlChararcter functions 
    //   use the regex matcher for all matchers??
    //   use a different thread to display the throbber?
    //   implement some way to iterate over all positions where a Matcher matches
    //   keep original size of the file and stop when that is reached 
    //       i.e. no endless loop on lgrep.dbg
    //            no endless loop when redirecting output to a file
    //   when lines are really long, find a way display only part of them
    //   fix xml search with the spinner (needs poper locks etc.)
    //   fix crash when run fro wsl.
    //   the lgrep.exe process does not always stop automatically on windows??, 
    //   perhaps when an exception is thrown -> call thread.abort

    List<string>searchDirectories       = new List<string>(10);
    List<string>searchMasks             = new List<string>(10);
    List<string>searchStrings           = new List<string>(10);
    SearchMode searchMode               = SearchMode.all;
    bool   caseInsensitive              = false;
    bool   inputRedirected              = false;
    bool   recurseDirectories           = false;
    bool   showBinary                   = true;
    public static bool showCount        = false;
    public static bool showFileName     = true;
    public static bool showFileNameOnce = false;
    public static bool showLineNumbers  = false;
    bool   showNonMatching              = false;
    bool   showOnlyFileName             = false;
    public static bool showProgress     = false;
    bool   xmlPrettyPrint               = false;
    int    afterContextPrintLines       = 0;
    int    beforeContextPrintLines      = 0;
    long   skipBytes                    = 0;
    long   skipPerc                     = 0;
    long   startAtLine                  = 0;
    long   stopAfterLine                = 0;
    string afterContextUntilString      = null;
    string beforeContextUntilString     = null;
    string codeModeFileName             = null;
    string fileListFile                 = null;
    string searchMask                   = null;
    string searchString                 = null;
    string searchStringFile             = null;
    string xpathQuery                   = null;

#if EXTERNALCODE 
    string codeWalkerFileName       = null;
#endif

    public static ConsoleColor foregroundColor        = ConsoleColor.Red;
    public static ConsoleColor backgroundColor        = Console.BackgroundColor;
    public static ConsoleColor fileForegroundColor    = ConsoleColor.White;
    public static ConsoleColor fileBackgroundColor    = Console.BackgroundColor;
    public static ConsoleColor controlForegroundColor = ConsoleColor.Yellow;
    public static ConsoleColor controlBackgroundColor = Console.BackgroundColor;

    FixedSizedQueue<string> contextQueue;

    // Debug
    bool debug = false;
    bool printUsageAndStop = false;

    public static Matcher m;

    public static void Main (string[] args)
    {
        grepper g;
#if DEBUG
        //TextWriterTraceListener tr1 = new TextWriterTraceListener(System.Console.Out);
        //Debug.Listeners.Add(tr1);
        TextWriterTraceListener tr2 = new TextWriterTraceListener(System.IO.File.CreateText("lgrep.dbg"));
        Debug.Listeners.Add(tr2);
#endif

        Console.OutputEncoding = Encoding.UTF8;
        Console.CancelKeyPress += new ConsoleCancelEventHandler(cancelKeyPressHandler);

        try {
            List<string> arguments = new List<string>();

            // get options from lgreprc
            string homeDir = Environment.GetEnvironmentVariable("HOME");
            string rcFileName;
            Debug.WriteLine(String.Format("Home directory: {0}", homeDir));

            try {
                rcFileName = Path.Combine(homeDir, ".lgreprc");
                if (File.Exists(rcFileName)) {
                    char[] charSeparators = new char[] {'\n','\r',' ','\t'};
                    string rcOptions = File.ReadAllText(rcFileName);
                    string[] rcOptionsList = rcOptions.Split(charSeparators, StringSplitOptions.RemoveEmptyEntries);
                    Debug.WriteLine(String.Format("Options read from lgreprc : {0}", string.Join("|", rcOptionsList)));
                    arguments.AddRange(rcOptionsList);
                }
            } catch (Exception) {
                // TODO: On windows this seems to throw an argument exception, need to find out why exactly.
            }

            // string exeDir = System.Reflection.Assembly.GetEntryAssembly().Location;
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            Debug.WriteLine(String.Format("Exe directory: {0}", exeDir));

            try {
                rcFileName = Path.Combine(exeDir, ".lgreprc");
                if (File.Exists(rcFileName)) {
                    char[] charSeparators = new char[] {'\n','\r',' ','\t'};
                    string rcOptions = File.ReadAllText(rcFileName);
                    string[] rcOptionsList = rcOptions.Split(charSeparators, StringSplitOptions.RemoveEmptyEntries);
                    Debug.WriteLine(String.Format("Options read from lgreprc : {0}", string.Join("|", rcOptionsList)));
                    arguments.AddRange(rcOptionsList);
                }
            } catch (Exception) { 
            }

            // get options from the environment
            string environmentOptions = Environment.GetEnvironmentVariable("LGREP_OPTIONS");
            if (environmentOptions != null && environmentOptions != "") {
                string[] environmentOptionsList = environmentOptions.Split();
                arguments.AddRange(environmentOptionsList);
                Debug.WriteLine(String.Format("Options read from LGREP_OPTIONS : {0}", string.Join("|", environmentOptions.Split())));
            }
            arguments.AddRange(args);

            g = new grepper(arguments.ToArray());
#if DEBUG        
            g.debug = true;
#endif
        }
        catch (System.IO.FileNotFoundException e) {
            ConsoleEx.CancelThrobber();
            ConsoleEx.WriteErrorLine(e.Message);
            return;
        }
        catch (ArgumentException a) {
            ConsoleEx.CancelThrobber();
            ConsoleEx.WriteErrorLine(a.Message);
            return;
        }

        if (g.printUsageAndStop) return;

        try {
            if (g.inputRedirected)
                g.RunStdIn();
            else
                g.Run(args);
        }
        catch (System.Security.SecurityException) {
            g.Run(args);
        }
        catch (Exception e) {
            ConsoleEx.WriteErrorLine("Uncaught error: {0}", e.Message);
            Util.PrintStackTrace(e);
            ConsoleEx.CancelThrobber();
#if DEBUG
            throw e;
#endif
        }

#if DEBUG
        Debug.WriteLine("Debug: Clean exit");
        Debug.Flush();
        Debug.Close();
#endif
    }

    private static bool IsOptionSwitch(string arg, string s)
    {
        if (arg.StartsWith(@"/"))
            return arg.Substring(1).Equals(s, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static bool IsTextSwitch(string arg, string s)
    {
        //Console.WriteLine("The entire argument is " + arg);
        if (arg.StartsWith(@"/")) {
            int colon = arg.IndexOf(":");
            if (colon < 0) colon = arg.IndexOf("=");

            if (colon < 0)
                return false;

            //Console.WriteLine("The options is " + arg.Substring(1, colon-1));
            if (arg.Substring(1, colon-1).Equals(s, StringComparison.OrdinalIgnoreCase)) {
                //Console.WriteLine("The text is" + arg.Substring(colon+1));
                return true;
            }
        }

        return false;
    }

    private static string TextSwitchGetValue(string arg)
    {
        if (arg.StartsWith(@"/")) {
            int colon = arg.IndexOf(":");
            if (colon < 0) colon = arg.IndexOf("=");

            if (colon < 0)
                return null;
            return arg.Substring(colon+1);
        }

        return null;
    }

    protected static void cancelKeyPressHandler(object sender, ConsoleCancelEventArgs args)
    {
        ConsoleEx.CancelThrobber();
        Console.WriteLine("Search aborted...");
        //Console.WriteLine("  Key pressed: {0}", args.SpecialKey);
        //Console.WriteLine("  Cancel property: {0}", args.Cancel);

        // Set the Cancel property to true to prevent the process from terminating.
        //Console.WriteLine("Setting the Cancel property to true...");
        //args.Cancel = true;
    }

    private static string PrettyXML(string XML)
    {
        string Result = "";

        using (MemoryStream mStream = new MemoryStream()) {
            using (XmlTextWriter writer = new XmlTextWriter(mStream, System.Text.Encoding.Default)) { // or Encoding.Unicode ...
                XmlDocument document = new XmlDocument();

                try
                {
                    // Load the XmlDocument with the XML.
                    document.LoadXml(XML);

                    writer.Formatting = Formatting.Indented;

                    // Write the XML into a formatting XmlTextWriter
                    document.WriteContentTo(writer);
                    writer.Flush();
                    mStream.Flush();

                    // Have to rewind the MemoryStream in order to read its contents.
                    mStream.Position = 0;

                    // Read MemoryStream contents into a StreamReader.
                    using (StreamReader sReader = new StreamReader(mStream)) {
                        // Extract the text from the StreamReader.
                        string FormattedXML = sReader.ReadToEnd();

                        Result = FormattedXML;
                    }
                }
                catch (XmlException)
                {
                }

                mStream.Close();
                writer.Close();
            }
        }

        return Result;
    }

    private grepper (string[] args)
    {
        // Parse arguments
        int i = 0;
        while (i < args.Length) {
            if (debug) {
                Debug.WriteLine(String.Format(" {0} {1}", i, args[i]));
            }

            if (IsOptionSwitch(args[i], "I")) {
                caseInsensitive = true;
            }
            else if (IsTextSwitch(args[i], "D")) {
                string dirs = TextSwitchGetValue(args[i]);
                foreach (string dir in dirs.Split(Path.PathSeparator)) {
                    searchDirectories.Add(dir);
                    Debug.WriteLine("Directories found to scan {0}", dir);
                }
            }
            else if (IsOptionSwitch(args[i], "P")) {
                showBinary = false;
            }
            // Search Mode
            else if (IsOptionSwitch(args[i], "B")) {
                searchMode = SearchMode.begin;
            }
            else if (IsOptionSwitch(args[i], "E")) {
                searchMode = SearchMode.end;
            }
            else if (IsOptionSwitch(args[i], "X")) {
                searchMode = SearchMode.wholeline;
            }
            else if (IsOptionSwitch(args[i], "R")) {
                searchMode = SearchMode.regex;
            }
            else if (IsTextSwitch(args[i], "C")) {
               searchString = TextSwitchGetValue(args[i]);
               searchStrings.Add(TextSwitchGetValue(args[i]));
            }
            else if (IsOptionSwitch(args[i], "R1")) {
                searchMode = SearchMode.regexUniLine;
            }
#if EXTERNALCODE
            else if (IsTextSwitch(args[i], "CS")) {
                searchMode = SearchMode.coded;
                codeModeFileName = TextSwitchGetValue(args[i]);
            }
            else if (IsTextSwitch(args[i], "CW")) {
                codeWalkerFileName = TextSwitchGetValue(args[i]);
            }
#endif
            //XML stuff
            else if (IsTextSwitch(args[i], "XP")) {
                xpathQuery = TextSwitchGetValue(args[i]);
                try
                {
                    //XPathExpression expr = XPathExpression.Compile(xpathQuery);
                    XPathExpression.Compile(xpathQuery);
                }
                catch (XPathException e)
                {
                    throw new ArgumentException(String.Format("Error compiling XPath expression ' {0}': {1}", xpathQuery, e.Message));
                }
            }
            else if (IsOptionSwitch(args[i], "XPP")) {
                xmlPrettyPrint = true;
            }
            else if (IsTextSwitch(args[i], "SP")) {
                skipPerc = Convert.ToInt64(TextSwitchGetValue(args[i]));
            }
            else if (IsTextSwitch(args[i], "PL")) {
                stopAfterLine = Convert.ToInt64(TextSwitchGetValue(args[i]));
            }
            else if (IsOptionSwitch(args[i], "M")) {
                showOnlyFileName = true;
            }
            else if (IsTextSwitch(args[i], "F")) {
                fileListFile = TextSwitchGetValue(args[i]);
            }
            else if (IsTextSwitch(args[i], "G")) {
                searchStringFile = TextSwitchGetValue(args[i]);
            }
            else if (IsOptionSwitch(args[i], "FN")) {
                showFileName = false;
            }
            else if (IsOptionSwitch(args[i], "FO")) {
                showFileNameOnce = true;
            }
            else if (IsOptionSwitch(args[i], "S")) {
                recurseDirectories = true;
            }
            else if (IsOptionSwitch(args[i], "N")) {
                showCount = true;
            }
            else if (IsOptionSwitch(args[i], "L")) {
                showLineNumbers = true;
            }
            else if (IsOptionSwitch(args[i], "progress")) {
                showProgress = true;
            }
            else if (IsTextSwitch(args[i], "spinner")) {
                ConsoleEx.spinner = new ConsoleSpinner(TextSwitchGetValue(args[i]), 130);
            }
            else if (IsTextSwitch(args[i], "fgcolor")
                    || IsTextSwitch(args[i], "bgcolor")
                    || IsTextSwitch(args[i], "ffgcolor")
                    || IsTextSwitch(args[i], "fbgcolor")
                    || IsTextSwitch(args[i], "cfgcolor")
                    || IsTextSwitch(args[i], "cbgcolor")
                    ) {
                // list of all colors:
                // Black Blue Cyan DarkBlue DarkCyan DarkGray DarkGreen DarkMagenta
                // DarkRed DarkYellow Gray Green Magenta Red White Yellow
                string argcolor = TextSwitchGetValue(args[i]);

                // Get an array with the values of ConsoleColor enumeration members.
                ConsoleColor[] colors = (ConsoleColor[]) ConsoleColor.GetValues(typeof(ConsoleColor));

                foreach (var color in colors) {
                    if (string.Compare(color.ToString(), argcolor, StringComparison.InvariantCultureIgnoreCase) == 0) {
                        if (IsTextSwitch(args[i], "fgcolor")) {
                            foregroundColor = color;
                        } else if (IsTextSwitch(args[i], "bgcolor")) {
                            backgroundColor = color;
                        } else if (IsTextSwitch(args[i], "ffgcolor")) {
                            fileForegroundColor = color;
                        } else if (IsTextSwitch(args[i], "fbgcolor")) {
                            fileBackgroundColor = color;
                        } else if (IsTextSwitch(args[i], "cfgcolor")) {
                            controlForegroundColor = color;
                        } else if (IsTextSwitch(args[i], "cbgcolor")) {
                            controlBackgroundColor = color;
                        }
                    }
                }
            }
            else if (IsOptionSwitch(args[i], "V")) {
                showNonMatching = true;
            }
            else if (IsOptionSwitch(args[i], "?")) {
                printUsageAndStop = true;
            }
            else if (IsTextSwitch(args[i], "lines")) {
                /* Console.WriteLine("checking out a lines textswitch"); */
                try {
                    string lineString = TextSwitchGetValue(args[i]);
                    int indexOfComma = lineString.IndexOf(",");
                    if (indexOfComma >= 0) {
                        string one = lineString.Substring(0, indexOfComma);
                        string two = lineString.Substring(indexOfComma + 1);
                        /* Console.WriteLine("one = {0}, two = {1}", one, two); */
                        startAtLine   = Convert.ToInt32(one);
                        stopAfterLine = Convert.ToInt32(two);
                        /* Console.WriteLine("one = {0}, two = {1}", startAtLine, stopAfterLine); */
                    } else {
                        string one = lineString;
                        startAtLine   = Convert.ToInt32(one);
                        stopAfterLine = Convert.ToInt32(one);
                    }
                }
                catch (System.FormatException f) {
                    throw new ArgumentException("Error processing lines argument: " + f.Message);
                }
            }
            else if (IsTextSwitch(args[i], "ca")) {
                try {
                    afterContextPrintLines = Convert.ToInt32(TextSwitchGetValue(args[i]));
                }
                catch (System.FormatException f) {
                    throw new ArgumentException("Context after argument error: " + f.Message);
                }
            }
            else if (IsTextSwitch(args[i], "cbs")) {
                beforeContextUntilString = TextSwitchGetValue(args[i]);
                if (beforeContextUntilString == null) {
                    ConsoleEx.WriteErrorLine("This option needs an argument {0}", args[i]);
                    printUsageAndStop = true;
                    return;
                }
            }
            else if (IsTextSwitch(args[i], "cas")) {
                afterContextUntilString = TextSwitchGetValue(args[i]);
                if (afterContextUntilString == null) {
                    ConsoleEx.WriteErrorLine("This option needs an argument {0}", args[i]);
                    printUsageAndStop = true;
                    return;
                }
            }
            else if (IsOptionSwitch(args[i], "debug")) {
                debug = true;
            }
            // FIXME: the first unkown option is used as a searchstring, instead an error should be displayed
            else if (searchString == null) {
                searchString = args[i];
                searchStrings.AddRange(searchString.Split());
            }
            else if (searchMask == null) {
                searchMask = args[i];
                searchMasks.Add(args[i]);
            }
            else {
                if (args[i].StartsWith(@"/") == true) {
                    throw new ArgumentException(String.Format("Unknown option: {0}", args[i]));
                }
                else {
                    searchMasks.Add(args[i]);
                    //throw new ArgumentException(String.Format("Excess argument: {0}", args[i]));
                }
            }

            i++;
        }

        // Can't show progress when the output is piped to another command
        inputRedirected = ConsoleEx.IsInputRedirected;

        // Read search strings from file
        Stream stream = null;
        if (searchStringFile != null ) {
            if (searchStringFile == "/") {
                stream = Console.OpenStandardInput();
                inputRedirected = false;
            } else if (searchStringFile != null) {
                stream = new FileStream(searchStringFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); 
            }

            try {
                using (StreamReader s = new StreamReader(stream, System.Text.Encoding.Default)) {
                    string text;
                    while ((text = s.ReadLine()) != null) {
                        Debug.WriteLine(String.Format("Adding file to scan {0}", text));
                        searchString = text;
                        searchStrings.AddRange(text.Split());
                    }
                }
            }
            finally {
                if (stream != null)
                    stream.Dispose();
            }
        }

        // Read file list from file
        stream = null;
        if (fileListFile != null) {
            if (fileListFile == "/") {
                stream = Console.OpenStandardInput();
                inputRedirected = false;
            } else if (fileListFile != null) {
                stream = new FileStream(fileListFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); 
            }

            try {
                using (StreamReader s = new StreamReader(stream, System.Text.Encoding.Default)) {
                    string text;
                    while ((text = s.ReadLine()) != null) {
                        Debug.WriteLine(String.Format("Adding SearchMask {0}", text));
                        searchMask = text;
                        searchMasks.AddRange(text.Split());
                    }
                }
            }
            finally {
                if (stream != null)
                    stream.Dispose();
            }
        }

        if (!printUsageAndStop) {
            if (searchString == null)
                throw new ArgumentException(String.Format("No Search string given..."));

            if (searchMask == null && inputRedirected == false)
                throw new ArgumentException(String.Format("No Search Mask given..."));
        }
        else {
            ConsoleEx.WriteErrorLine(
@"LGREP [/B] [/E] [/R] [/S] [/I] [/V] [/P] [/F:file]
strings [[drive:][path]filename[ ...]]


** Search switches **
/B Matches pattern if at the beginning of a line.
/E Matches pattern if at the end of a line.
/R Uses search strings as regular expressions.
/I Specifies that the search is not to be case-sensitive.

/V Prints only lines that do not contain a match.

/C:string   Uses specified string as a literal search string.

/CS:string  Uses specified string as a code to match a line.

** File Selection switches **
/S          Searches for matching files in the current directory and all
            subdirectories.
/P          Skip files with non-printable characters.
/CW:file    Use the treewalker coded in this file.


** Display switches **
/L          Prints the line number before each line that matches.
/N          Prints the number of matches.
/M          Prints only the filename if a file contains a match.
/FN         Don't print the filename if a file contains a match.

/PROGRESS   Show a progress bar of the scan

** Context switches **

/CA:number  Give n number of lines of context after any match
/CB:number  Give n number of lines of context before any match
/CAS:string Give context after any match until the appearence of this string
/CBS:string Give context before any match as from the appearence of this string

The string matching context function can be used in conjunction with a number
in which case the minimum of both conditions will apply. Standard limit to
string matching context is 1000 lines.

** Data scan switches **
/SB:number  Skip n bytes of each file before scanning for matches
/SP:number  Skip n percent of each file before scanning for matches
/PL:number  StoP after n lines in each file


** XML Mode **
/XP:string  Don't scan lines, but scan the result of an XPath query
            (see http://en.wikipedia.org/wiki/XPath)
/XPP        Pretty Print XML Content

Some options don't apply in Xml mode


********************

strings     Text to be searched for.
[drive:][path]filename
Specifies a file or files to search.

Use spaces to separate multiple search strings unless the argument is prefixed
with /C.  For example, 'LGREP ""hello there"" x.y' searches for ""hello"" or
""there"" in file x.y.  'LGREP /C:""hello there"" x.y' searches for
""hello there"" in file x.y.

Regular expression quick reference:
.        Wildcard: any character
*        Repeat: zero or more occurrences of previous character or class
^        Line position: beginning of line
$        Line position: end of line
[class]  Character class: any one character in set
[^class] Inverse class: any one character not in set
[x-y]    Range: any characters within the specified range
\x       Escape: literal use of metacharacter x
\<xyz    Word position: beginning of word
xyz\>    Word position: end of word

For full information on LGREP regular expressions refer to the online Command
Reference.");
            return;
        }

        Debug.WriteLine("-----------------------------------------------------");
        Debug.WriteLine(String.Format("searchMode               : {0}", searchMode));
        Debug.WriteLine(String.Format("searchString             : {0}", searchString ?? "<blank>"));
        Debug.WriteLine(String.Format("searchStrings            : {0}", string.Join("; ", searchStrings.ToArray())));
        Debug.WriteLine(String.Format("searchMask               : {0}", searchMask ?? "<blank>"));
        Debug.WriteLine(String.Format("searchMasks              : {0}", string.Join("; ", searchMasks.ToArray())));
        Debug.WriteLine(String.Format("xpathQuery               : {0}", xpathQuery ?? "<blank>"));
        Debug.WriteLine(String.Format("startAtLine              : {0}", startAtLine));
        Debug.WriteLine(String.Format("stopAfterLine            : {0}", stopAfterLine));
        Debug.WriteLine(String.Format("xmlPrettyPrint           : {0}", xmlPrettyPrint));

        Debug.WriteLine(String.Format("\nshowOnlyFileName         : {0}", showOnlyFileName));
        Debug.WriteLine(String.Format("showFileName             : {0}", showFileName));
        Debug.WriteLine(String.Format("showFileNameOnce         : {0}", showFileNameOnce));
        Debug.WriteLine(String.Format("caseInsensitive          : {0}", caseInsensitive));
        Debug.WriteLine(String.Format("showBinary               : {0}", showBinary));
        Debug.WriteLine(String.Format("showNonMatching          : {0}", showNonMatching));
        Debug.WriteLine(String.Format("showProgress             : {0}", showProgress));
        Debug.WriteLine(String.Format("recurseDirectories       : {0}", recurseDirectories));
        Debug.WriteLine(String.Format("showLineNumbers          : {0}", showLineNumbers));
        try {
            Debug.WriteLine(String.Format("From pipe                : {0}", ConsoleEx.IsInputRedirected));
            Debug.WriteLine(String.Format("To pipe                  : {0}", ConsoleEx.IsOutputRedirected));
        }
        catch (System.Security.SecurityException) {
            Debug.WriteLine("From pipe/to pipe not possible");
        }

        Debug.WriteLine(String.Format("\nbeforeContextPrintLines  : {0}", beforeContextPrintLines));
        Debug.WriteLine(String.Format("afterContextPrintLines   : {0}", afterContextPrintLines));
        Debug.WriteLine(String.Format("beforeContextUntilString : {0}", beforeContextUntilString ?? "<blank>"));
        Debug.WriteLine(String.Format("afterContextUntilString  : {0}", afterContextUntilString ?? "<blank>"));

        Debug.WriteLine(String.Format("stopAfterLine            : {0}", stopAfterLine));
        Debug.WriteLine(String.Format("skipBytes                : {0}", skipBytes));
        Debug.WriteLine(String.Format("skipPerc                 : {0}", skipPerc));
        Debug.WriteLine(String.Format("command line args        : {0}", String.Join(" ", args)));
        Debug.WriteLine("-----------------------------------------------------");

        Debug.WriteLine(String.Format("foregroundColor          : {0}", foregroundColor    ));
        Debug.WriteLine(String.Format("backgroundColor          : {0}", backgroundColor    ));
        Debug.WriteLine(String.Format("fileForegroundColor      : {0}", fileForegroundColor));
        Debug.WriteLine(String.Format("fileBackgroundColor      : {0}", fileBackgroundColor));
        Debug.WriteLine("-----------------------------------------------------");

        // If context strings are provided without counts, we set up some defaults
        if (beforeContextUntilString != null & beforeContextPrintLines == 0) {
            beforeContextPrintLines = 1000;
            Debug.WriteLine(String.Format("Putting beforeContextPrintLines to its default value {0}", beforeContextPrintLines));
        }

        if (afterContextUntilString != null & afterContextPrintLines == 0) {
            afterContextPrintLines = 1000;
            Debug.WriteLine(String.Format("Putting afterContextPrintLines to its default value {0}", afterContextPrintLines));
        }

        // Can't show progress when the output is piped to another command
        if (ConsoleEx.IsOutputRedirected)
            showProgress = false;

        // Contextqueue
        contextQueue = new FixedSizedQueue<string>(beforeContextPrintLines);

        // Create the matcher
        if (searchStrings.Count > 0) {
            ComboMatcher cm = new ComboMatcher();
            foreach(string str in searchStrings) {
                cm.Add(MatcherFactory.Create(searchMode, str, caseInsensitive, codeModeFileName));
            }
            m = cm;
        } else {
            m = MatcherFactory.Create(searchMode, searchString, caseInsensitive, codeModeFileName);
        } 

        if (m == null & searchMode != SearchMode.regexUniLine) {
            ConsoleEx.WriteErrorLine("System error, search mode unknown or could not allocate new object");
            printUsageAndStop = true;
            return;
        }
    }

    private void Run (string[] args)
    {
        long filesScanned = 0;

        try {
            TreeWalker tw;
            if (searchDirectories.Count == 0) {
                searchDirectories.Add(Directory.GetCurrentDirectory());
            } 
#if EXTERNALCODE
            if (codeWalkerFileName != null) {
                CodedTreeWalker ctw = new CodedTreeWalker(Directory.GetCurrentDirectory(), recurseDirectories, searchMask, codeWalkerFileName);
                //tw = (TreeWalker) ctw._o;
                tw = ctw;
            } else {
                tw = new TreeWalker(searchDirectories, recurseDirectories, searchMasks);
            }

#else
            tw = new TreeWalker(searchDirectories, recurseDirectories, searchMasks);
#endif

            foreach (string file in tw) {
                filesScanned++;

                if (searchMode == SearchMode.regexUniLine) {
                    string Content = File.ReadAllText(file);
                    MatchCollection matchList = Regex.Matches(Content, searchString, RegexOptions.Singleline);
                    foreach (Match match in matchList) {
                        Console.WriteLine("{0}", match.Value);
                    }
                } else {
                    try {
                        using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                            System.Text.Encoding encoding = System.Text.Encoding.Default;

                            /* Check if it is a binary file */
                            if (showBinary == false && IsBinary (f)) {
                                filesScanned--;
                                continue;
                            }

                            /* Special case xpath -> process the file in XML mode. */
                            if (xpathQuery != null)
                                ProcessXMLFile(file, f);
                            else {
                                if (skipPerc > 0)
                                    f.Seek((long) ((double)skipPerc / 100.0 * f.Length), SeekOrigin.Begin);
                                else
                                    f.Seek (skipBytes, SeekOrigin.Begin);

                                using (StreamReader s = new LStreamReader(f, startAtLine, stopAfterLine)) {
                                    ProcessWithReader(s, file, f);
                                }
                            }
                        }
                    }
                    catch (Exception e) {
                        ConsoleEx.WriteErrorLine("\rError while processing {0}: {1}", file, e.Message);
#if DEBUG
                        ConsoleEx.WriteErrorLine(e.ToString());
#endif
                    }
                }
            }

            if (showProgress) {
                ConsoleEx.FlushStatusLine();
            }
        }
        catch (Exception e) {
            StackTrace st = new StackTrace();
            StackFrame sf = st.GetFrame(0);
            ConsoleEx.WriteErrorLine("");
            ConsoleEx.WriteErrorLine("Exception raised {0}: {1}", e, e.Message);
            ConsoleEx.WriteErrorLine("  Exception in method: ");
            ConsoleEx.WriteErrorLine("      {0}", sf.GetMethod());

            if (st.FrameCount > 1)
            {
                // Display the highest-level function call  
                // in the trace.
                sf = st.GetFrame(st.FrameCount-1);
                ConsoleEx.WriteErrorLine("  Original function call at top of call stack):");
                ConsoleEx.WriteErrorLine("      {0}", sf.GetMethod());
            }

            throw;
        }
        finally {
            ConsoleEx.CancelThrobber();
        }

        if (filesScanned == 0) {
            ConsoleEx.CancelThrobber();
            Console.Error.WriteLine("No files processed.");
        }
    }

    private void RunStdIn()
    {
        // Special case xpath
        if (xpathQuery != null) {
            ProcessXMLFile("<stdin>", Console.OpenStandardInput());
        } else {
            using (StreamReader s = new StreamReader(Console.OpenStandardInput(), System.Text.Encoding.Default)) {
                ProcessWithReader(s, "<stdin>", null);
            }
        }
    }

    private void ProcessXMLFile(string file, Stream f) {
        string formattedFileName = Util.FormatFileName(file);
        Debug.WriteLine(String.Format("in xpath {0} {1} {2}", xpathQuery, searchString, searchMode));

        XmlDocument doc = new XmlDocument();
        try {

            string s;

            if (caseInsensitive) {
                using (StreamReader reader = new StreamReader(f))
                {
                    // Convert stream tags to lower case
                    s = reader.ReadToEnd();
                    //s = Regex.Replace(s, @"</?.*?>", myToLower);
                    //s = Regex.Replace(s, @"</?\w+(\s+\w+="".*?"")*>", myToLower);
                    s = Regex.Replace(s, @"</?\w+(\s+\w+="".*?"")*>", x => x.ToString().ToLower());
                    doc.LoadXml(s);
                }
            }
            else
            {
                doc.Load(f);
            }
        }
        catch (System.Xml.XmlException e) {
            ConsoleEx.WriteErrorLine(file + ": " + e.Message);
            f.Seek(0, SeekOrigin.Begin);
            doc = new XmlDocument();
            doc.Load(new InvalidXmlCharacterReplacingStreamReader (f, ' '));
        }

        XmlNode root = doc.DocumentElement;

        // Add the namespace.
        //XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
        //nsmgr.AddNamespace("bk", "urn:newbooks-schema");

        // Select all nodes where the book price is greater than 10.00.
        //XmlNodeList nodeList = root.SelectNodes(searchString, nsmgr);
        XmlNodeList nodeList = root.SelectNodes(xpathQuery);
        foreach (XmlNode node in nodeList)
        {
            // TODO: fix this, it is not thread safe
            if (m.matches (node.OuterXml)) {
                if (showOnlyFileName) {
                    ConsoleEx.WriteLine("\r{0}", formattedFileName);
                    break;
                }

                if (!showNonMatching) {
                    if (showFileName == true)
                        ConsoleEx.SetFileColor();
                        ConsoleEx.Write ("{0}:", formattedFileName);
                        ConsoleEx.ResetColor();

                    if (xmlPrettyPrint) {
                        ConsoleEx.WriteLine("", formattedFileName);
                        m.printLineColor(PrettyXML(node.OuterXml));
                    }
                    else {
                        m.printLineColor(node.OuterXml);
                    }
                }
            } else {
                if (showNonMatching) {
                    if (showFileName == true) {
                        ConsoleEx.SetFileColor();
                        ConsoleEx.Write ("\r{0}:", formattedFileName);
                        ConsoleEx.ResetColor();
                    }
                    m.printLineColor(node.OuterXml);
                }
            }
        }
    }

    private void ProcessWithReader(StreamReader s, string file, FileStream f) {
        long currentLineNumber = 0;
        int  afterMatchContext = 0;
        int  matchCount        = 0;
        bool fileNamePrinted   = false;
        string text;

        LStreamReader lfr = s as LStreamReader;
        if (lfr != null) currentLineNumber = lfr.CurrentLine;

        if (showProgress) {
            ConsoleEx.StatusLine = string.Format("processing file {0}", Util.FormatFileName(file));
        }

        while ((text = s.ReadLine()) != null) {
            currentLineNumber++;
            /* Thread.Sleep(10); */

            // TODO: what if f == null? Is that the case when reading from stdin?
            // Is this line slowing down the search when /progress is activated?
            if (showProgress && f != null) { 
                ConsoleEx.StatusLine = string.Format("processing file ({0:P0}) {1}", (double)f.Position/(double)f.Length, Util.FormatFileName(file));
            }

            if (m.matches (text) ^ showNonMatching) {
                if (!fileNamePrinted && (showOnlyFileName || showFileNameOnce && !showCount)) {
                    fileNamePrinted = true;
                    if (showOnlyFileName) {
                        ConsoleEx.WriteLine("{0}", Util.FormatFileName(file));
                        break;
                    }
                    ConsoleEx.SetFileColor();
                    ConsoleEx.WriteLine("{0}", Util.FormatFileName(file));
                    ConsoleEx.ResetColor();
                }

                // print context: look for the appearance of the string and remove the part before if found
                if (beforeContextUntilString != null) {
                    int matches = 0;
                    int lineno = 0;
                    foreach (string line in contextQueue) {
                        lineno++;
                        if (line.IndexOf(beforeContextUntilString, StringComparison.CurrentCultureIgnoreCase) >= 0)
                            matches = lineno;
                    }

                    for (int i = 0; i < matches - 1; i++)
                        contextQueue.Dequeue();
                }

                // print context 
                // There is a bug with the line number when the
                // /CBS is used.  In combination with context before and after,
                // we may print the same line twice.
                // TODO: is that still the case?
                while (contextQueue.Count > 0) {
                    long lineCount = currentLineNumber - contextQueue.Count;
                    ConsoleEx.WriteLine(contextQueue.Dequeue(), file, lineCount);
                }

                // Write file name and line number
                matchCount++;
                ConsoleEx.WriteMatchLine(text, file, currentLineNumber);

                afterMatchContext = afterContextPrintLines;
            } else {
                // Add this line to the context queue
                contextQueue.Enqueue(text);

                // If we need to print context after a match, we do it here
                if (afterMatchContext > 0) {
                    ConsoleEx.WriteMatchLine(text, file, currentLineNumber);
                    afterMatchContext--;

                    if (afterContextUntilString != null && text.IndexOf(afterContextUntilString, StringComparison.CurrentCultureIgnoreCase) >= 0) {
                        afterMatchContext = 0;
                    }
                }
            }
        }

        // Print the number of matches in this file.
        if (showCount && matchCount > 0) {
            if (matchCount == 1)
                ConsoleEx.WriteLine(String.Format("{0}: 1 match", Util.FormatFileName(file)));
            else
                ConsoleEx.WriteLine(String.Format("{0}: {1} matches", Util.FormatFileName(file), matchCount));
        }
    }

    public static CompilerResults compileExternalFile(string filename)
    {
        CompilerResults results;
        string code;

        using (FileStream f = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
            using (StreamReader s = new StreamReader(f, System.Text.Encoding.Default)) {
                code = s.ReadToEnd();
            }
        }

        CodeDomProvider provider;
        provider = new CSharpCodeProvider();
        if (Path.GetExtension(filename) == ".cs") {
            Debug.WriteLine("compiling C# file");
            provider = new CSharpCodeProvider();
        }

        if (Path.GetExtension(filename) == ".vb") {
            Debug.WriteLine("compiling VB file");
            provider = new VBCodeProvider();
        }

        //CSharpCodeProvider provider = new CSharpCodeProvider(new Dictionary<String, String> { { "CompilerVersion", "v3.5" } });
        //CSharpCodeProvider provider = new CSharpCodeProvider();
        CompilerParameters compilerparams = new CompilerParameters();
        compilerparams.GenerateExecutable = false;
        compilerparams.GenerateInMemory = true;

        // And set any others you want, there a quite a few, take some time to
        // look through them all and decide which fit your application best!
        // Add any references you want the users to be able to access, be
        // warned that giving them access to some classes can allow harmful
        // code to be written and executed. I recommend that you write your own
        // Class library that is the only reference it allows thus they can
        // only do the things you want them to.  (though things like
        // "System.Xml.dll" can be useful, just need to provide a way users can
        // read a file to pass in to it) Just to avoid bloatin this example to
        // much, we will just add THIS program to its references, that way we
        // don't need another project to store the interfaces that both this
        // class and the other uses. Just remember, this will expose ALL public
        // classes to the "script"
        compilerparams.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);

        results = provider.CompileAssemblyFromSource(compilerparams, code);

        foreach(CompilerError e in results.Errors) {
            Console.WriteLine(e);
        }

        return results;
    }

    public static Type getClassFromCompilerResults(CompilerResults results, Type klass)
    {
        foreach (Type t in results.CompiledAssembly.GetTypes())
        {
            if (t.IsClass && t.IsSubclassOf(klass)) {
                return t;
            }
        }

        return null;
    }

    // TODO: use some IsControl Character Unicode classes instead?
    private static bool IsBinary (byte[] bytes, int maxLength)
    {
        int len = maxLength > 1024 ? 1024 : maxLength;

        int nonASCIIcount = 0;
        int zeroscount = 0;

        for (int i = 0; i < len; ++i) {
            byte[] b = new byte[1];
            b [0] = bytes [i];
            //Console.WriteLine("Char {0}, {1}", System.Text.Encoding.ASCII.GetString(b), (int)bytes[i]);
            if ((int)bytes [i] > 127) {
                ++nonASCIIcount;
            }

            if ((int)bytes [i] == 0) {
                ++zeroscount;
            }
        }

        // if the number of non ASCII is more than a 30%
        // then is a binary file.
#if DEBUG
        //Debug.WriteLine(String.Format("Percentage of non ASCII characters {0}", nonASCIIcount));
        //Debug.WriteLine(String.Format("Percentage of zeros characters {0}", zeroscount));
#endif
        double result = (double)nonASCIIcount / (double)len;
#if DEBUG
        //Debug.WriteLine(String.Format("Result = {0}, {1}", result, (result > 0.01)));
#endif
        return (result > 0.01);
    }

    private static bool IsBinary (Stream f)
    {
        byte[] buffer = new byte[1024];
        int numread;
        long orig_position;

        orig_position = f.Position;
        f.Seek(0, SeekOrigin.Begin);
        numread = f.Read (buffer, 0, 1024);
        f.Seek(orig_position, SeekOrigin.Begin);
        return (IsBinary (buffer, numread));
    }

    private static bool IsBinary (string file)
    {
        byte[] buffer = new byte[1024];
        int numread;
        using (FileStream f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            numread = f.Read (buffer, 0, 1024);
            f.Close ();
            return (IsBinary (buffer, numread));
        }
    }
}

public class ConsoleSpinner
{
    int delay;
    string[] sequence;
    Stopwatch stopwatch;
    int prevTicks;
    int currentSequence = 0;

    //public ConsoleSpinner(string sSequence = "dots", int iDelay = 100, bool bLoop = false)
    public ConsoleSpinner(string sSequence, int iDelay)
    {
        delay = iDelay;
        if (sSequence == "dots")
            sequence = new string[] { ". ", ".. ", "... ", "...." };
        else if (sSequence == "arrows")
            sequence = GetStringArray("←↖↑↗→↘↓↙");
        else if (sSequence == "slashes")
            sequence = new string[] { "/", "-", "\\", "|" };
        else if (sSequence == "circles")
            sequence = new string[] { ".", "o", "O", "°", "o" };
        else if (sSequence == "crosses")
            sequence = new string[] { "+", "x" };
        else if (sSequence == "arrows")
            sequence = new string[] { "V", "<", "^", ">" };
        else if (sSequence == "boobs")
            sequence = new [] { "(.)(.)", "(.)(o)", "(.)(°)", "(.)(o)", "(.)(.)", "(o)(.)", "(°)(.)", "(o)(.)", "(.)(.)", "(.)(.)", "(o)(o)", "(o)(o)", "(*)(*)", "(*)(*)", "(.)(.)", "(.)(.)"};
        else if (sSequence == "bars")
            sequence = GetStringArray("▁▃▄▅▆▇█▇▆▅▄▃");
            /* sequence = "▁▃▅▆▇▇▆▅▃" */
        else if (sSequence == "flip")
            sequence = new [] {"_", "_", "_", "-", "`", "`", "'", "´", "-", "_", "_", "_"};
        else if (sSequence == "dots2")
            sequence = GetStringArray("⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏");
        else if (sSequence == "dots3")
            sequence = new [] {"(*---------)",
                               "(-*--------)",
                               "(--*-------)",
                               "(---*------)",
                               "(----*-----)",
                               "(-----*----)",
                               "(------*---)",
                               "(-------*--)",
                               "(--------*-)",
                               "(---------*)",
                               "(--------*-)",
                               "(-------*--)",
                               "(------*---)",
                               "(-----*----)",
                               "(----*-----)",
                               "(---*------)",
                               "(--*-------)",
                               "(-*--------)"};
        else
            sequence = new string[] { "/", "-", "\\", "|" };

        stopwatch = new Stopwatch();
        stopwatch.Start();
    }

    private string[] GetStringArray(string instr) {
        return instr.ToCharArray().Select(c => c.ToString()).ToArray();
    }

    public bool hasChanged()
    {
        if ((prevTicks / delay) % sequence.Length != (stopwatch.Elapsed.Milliseconds / delay) % sequence.Length)
            return true;
        return false;
    }

    public string getSpinner()
    {
        int ticks = stopwatch.Elapsed.Milliseconds / delay;
        prevTicks = stopwatch.Elapsed.Milliseconds;

        return sequence[ticks % sequence.Length];
    }

    public void Tick() {
        currentSequence++;
    }

    public string GetCurrent() {
        return sequence[currentSequence % sequence.Length];
    }

    public string GetNext() {
        ConsoleEx.WriteLine("spinner {0}", currentSequence);
        return sequence[currentSequence++ % sequence.Length];
    }
}

public class TreeWalker : IEnumerable<string>
{
    public class TreeWalkerPattern {
        public string pattern;
        public bool isGlob;
        public Regex regex;

        public TreeWalkerPattern(string p) {
            pattern = p;
            if (pattern.IndexOf("*") >= 0 | pattern.IndexOf("?") >= 0)
                isGlob = true;
            else
                isGlob = false;

            // TODO: Why do we need the single line regex option here?
            if (isGlob) {
                regex = new Regex(
                        "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                        RegexOptions.Compiled | RegexOptions.IgnoreCase);
            } else {
                regex = null;
            }
        }

        public TreeWalkerPattern(string p, bool i, Regex r) {
            pattern = p;
            isGlob = i;
            regex = r;
        }

        public bool Matches(string fileName) {
            if (isGlob) {
                Debug.WriteLine("TreeWalkerPattern: Glob given " + pattern + " / " + fileName );
                if (regex.IsMatch(fileName)) return true;
            } else {
                Debug.WriteLine("TreeWalkerPattern: Filename given " + pattern + " / " + fileName );
                if (pattern.Equals(fileName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    };

    List<string> directoriesToScan = new List<string>();
    List<TreeWalkerPattern> patterns = new List<TreeWalkerPattern>();
    bool recurse;

    public TreeWalker (): this(".", false, "") {
    }

    public TreeWalker (string r, bool recurseDirectories, string globPattern):
        this(new List<string>(new string[] { r }),
                recurseDirectories,
                new List<string>(new string[] { globPattern })) {
        }

    public TreeWalker (List<string> r, bool recurseDirectories, List<string> globPatterns)
    {
        foreach (string d in r) {
            string d2 = d.TrimEnd(Path.DirectorySeparatorChar);
            string direname = Path.GetDirectoryName(d2);
            string basename = Path.GetFileName(d2);

            var pattern = new TreeWalkerPattern(basename);
            Debug.WriteLine(String.Format("TreeWalker: adding directory {0} : {1}", direname, basename));

            if (pattern.isGlob) {
                foreach(string dir in System.IO.Directory.GetDirectories(direname)) {
                    if (pattern.Matches(Path.GetFileName(dir))) {
                        directoriesToScan.Add(dir);
                    }
                }
            } else {
                directoriesToScan.Add(d);
            }
        }

        recurse = recurseDirectories;
        foreach (string globPattern in globPatterns) {
            patterns.Add(new TreeWalkerPattern(globPattern));
        }
    }

    public virtual bool FileMatches(string str)
    {
        string basename = Path.GetFileName(str);

        foreach (TreeWalkerPattern p in patterns) {
            if (p.Matches(basename)) {
                return true;
            }
        }
        return false;
    }

    public IEnumerator<String> GetEnumerator ()
    {
        // Data structure to hold names of subfolders to be examined for files.
        Stack<string> dirs = new Stack<string> (20);
        bool match;
        Debug.WriteLine(String.Format("TreeWalker: GetEnumerator: {0}", String.Join(";", directoriesToScan.Distinct())));

        foreach (string dir in directoriesToScan.Distinct()) {
            dirs.Push(dir);
        }

        ConsoleEx.StatusLine = "scanning directories";

        while (dirs.Count > 0) {
            string currentDir = dirs.Pop();
            string[] subDirs;

            ConsoleEx.StatusLine = "scanning directory " + currentDir;

            try {
                subDirs = System.IO.Directory.GetDirectories (currentDir);
            }
            // An UnauthorizedAccessException exception will be thrown if we do not have 
            // discovery permission on a folder or file. It may or may not be acceptable  
            // to ignore the exception and continue enumerating the remaining files and  
            // folders. It is also possible (but unlikely) that a DirectoryNotFound exception  
            // will be raised. This will happen if currentDir has been deleted by 
            // another application or thread after our call to Directory.Exists. The  
            // choice of which exceptions to catch depends entirely on the specific task  
            // you are intending to perform and also on how much you know with certainty  
            // about the systems on which this code will run. 
            catch (UnauthorizedAccessException e) {                    
                ConsoleEx.WriteErrorLine(e.Message);
                continue;
            } catch (System.IO.DirectoryNotFoundException e) {
                ConsoleEx.WriteErrorLine(e.Message);
                continue;
            }

            string[] files = null;
            try {
                files = System.IO.Directory.GetFiles (currentDir);
            } catch (UnauthorizedAccessException e) {
                ConsoleEx.WriteErrorLine(e.Message);
                continue;
            } catch (System.IO.DirectoryNotFoundException e) {
                ConsoleEx.WriteErrorLine(e.Message);
                continue;
            }

            foreach (string file in files) {
                try {
                    match = FileMatches(file);
                } catch (Exception e) {
                    ConsoleEx.WriteErrorLine("Exception thrown when assessing to scan file {0}: {1}", file, e.Message);
                    match = false;
                }

                if (match) {
                    Debug.WriteLine(String.Format("Scanning file {0}", file));
                    ConsoleEx.StatusLine = "scanning file " + Util.FormatFileName(file);
#if DEBUG
                    // avoid endless loop when debugging
                    if (file.Contains("lgrep.dbg")) continue;
#endif
                    yield return file;
                } else {
                    Debug.WriteLine(String.Format("Skipping file {0}", Util.FormatFileName(file)));
                }
            }

            // Push the subdirectories onto the stack for traversal. 
            if (recurse) {
                foreach (string str in subDirs)
                    dirs.Push (str);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator ()
    {
        return GetEnumerator ();
    }
}

#if EXTERNALCODE
public class CodedTreeWalker: TreeWalker
{
    CompilerResults results;
    Type externalWalkerType = null;
    public TreeWalker _o = null;

    public CodedTreeWalker (string r, bool recurseDirectories, string globPattern, string filename): base(r, recurseDirectories, globPattern)
    {
        results = grepper.compileExternalFile(filename);
        externalWalkerType = grepper.getClassFromCompilerResults(results, typeof(TreeWalker));
        System.Reflection.ConstructorInfo constructor = externalWalkerType.GetConstructor (new Type[] { Type.GetType("System.String")});

        if (constructor != null) _o = (TreeWalker) constructor.Invoke(new object[] {globPattern});
        else {
            constructor = externalWalkerType.GetConstructor (new Type[] {});
            _o = (TreeWalker) constructor.Invoke(new object[] {});
        }
    }

    public override bool FileMatches (string s)
    {
        return _o.FileMatches(s);
    }
}
#endif

public class MatcherFactory
{
    public static Matcher Create(SearchMode searchMode, string searchString, bool caseInsensitive, string codeModeFileName)
    {
        Matcher m = null;
        switch (searchMode) {
            case SearchMode.all:
                m = new StringMatcher (searchString, caseInsensitive);
                break;
            case SearchMode.begin:
                m = new BeginMatcher (searchString, caseInsensitive);
                break;
            case SearchMode.end:
                m = new EndMatcher (searchString, caseInsensitive);
                break;	
            case SearchMode.wholeline:
                m = new WholeLineMatcher (searchString, caseInsensitive);
                break;
            case SearchMode.regex:
                m = new RegexMatcher (searchString, caseInsensitive);
                break;
#if EXTERNALCODE
            case SearchMode.coded:
                m = new ExternalCodeMatcher (codeModeFileName, searchString, caseInsensitive);
                break;
#endif
        }
        return m;
    }
}

public abstract class Matcher
{
    public abstract bool matches (string s);
    public virtual void printLine (string text)
    {
        Console.WriteLine(text);
    }

    public abstract void printLineColor (string text);
}

public class ComboMatcher: Matcher
{
    List<Matcher> matchers;

    public ComboMatcher ()
    {
        matchers = new List<Matcher>();
    }

    public void Add(Matcher m) 
    {
        matchers.Add(m);
    }

    public override bool matches (string s)
    {
        foreach (Matcher m in matchers) {
            if (m.matches(s))
                return true;
        }
        return false;
    }

    // TODO: make this work with matches from all submatchers
    public override void printLineColor (string text)
    {
        foreach (Matcher m in matchers) {
            if (m.matches(text)) {
                m.printLineColor(text);
                return;
            }
        }
        printLine(text);
    }
}

public class StringMatcher: Matcher
{
    protected string searchString;
    protected StringComparison scase;

    public StringMatcher (string _searchString, bool caseInsensitive)
    {
        searchString = _searchString;
        if (caseInsensitive)
            scase = StringComparison.CurrentCultureIgnoreCase;
        else
            scase = StringComparison.CurrentCulture;
    }

    public override bool matches (string s)
    {
        if (s.IndexOf (searchString, scase) >= 0)
            return true;
        else
            return false;
    }

    public override void printLineColor (string text)
    {
        int ind;
        if (searchString == "") {
            Console.WriteLine(text);
            return;
        }

        try {
            while ((ind = text.IndexOf(searchString, scase)) >= 0) {
                ConsoleEx.ResetColor();
                Console.Write ("{0}", text.Substring (0, ind));

                ConsoleEx.SetMatchColor();
                Console.Write ("{0}", text.Substring (ind, searchString.Length));

                text = text.Substring (ind + searchString.Length);
            }

            ConsoleEx.ResetColor();
            if (text.Length > 0)
                Console.WriteLine(text);
            else
                Console.Write (Environment.NewLine);
        }
        catch (Exception) {
            //StackTrace st = new StackTrace(ex, true);
            ConsoleEx.WriteErrorLine(text);
        }		
    }
}

class BeginMatcher: StringMatcher
{
    public BeginMatcher (string _searchString, bool caseInsensitive): base(_searchString, caseInsensitive)
    {
    }

    public override bool matches (string s)
    {
        if (s.StartsWith (searchString, scase))
            return true;
        else
            return false;
    }

    public override void printLineColor (string text)
    {
        if (searchString == "") {
            Console.WriteLine(text);
            return;
        }

        ConsoleEx.SetMatchColor();
        Console.Write ("{0}", text.Substring (0, searchString.Length));

        ConsoleEx.ResetColor ();
        Console.Write ("{0}", text.Substring (searchString.Length));
        Console.Write (Environment.NewLine);
    }
}

class EndMatcher: StringMatcher
{
    public EndMatcher (string _searchString, bool caseInsensitive): base(_searchString, caseInsensitive)
    {
    }

    public override bool matches (string s)
    {
        if (s.EndsWith (searchString, scase))
            return true;
        else
            return false;
    }

    public override void printLineColor (string text)
    {
        if (searchString == "") {
            Console.WriteLine(text);
            return;
        }

        int ind = text.LastIndexOf (searchString, scase);

        ConsoleEx.ResetColor ();
        Console.Write ("{0}", text.Substring (0, ind));

        ConsoleEx.SetMatchColor();
        Console.Write ("{0}", text.Substring (ind));
        Console.Write (Environment.NewLine);

        ConsoleEx.ResetColor();
    }
}

class WholeLineMatcher: StringMatcher
{
    public WholeLineMatcher (string _searchString, bool caseInsensitive): base(_searchString, caseInsensitive)
    {
    }

    public override bool matches (string s)
    {
        if (s.Equals (searchString, scase))
            return true;
        else
            return false;
    }

    public override void printLineColor (string text)
    {
        if (searchString == "") {
            Console.WriteLine(text);
            return;
        }

        int ind = text.LastIndexOf (searchString, scase);

        ConsoleEx.ResetColor();
        Console.Write ("{0}", text.Substring (0, ind));

        ConsoleEx.SetMatchColor();
        Console.Write ("{0}", text.Substring (ind));
        Console.Write (Environment.NewLine);

        ConsoleEx.ResetColor();
    }
}

class RegexMatcher: Matcher
{
    Regex regex;

    public RegexMatcher (string _searchString, bool caseInsensitive)
    {
        RegexOptions options = RegexOptions.Compiled;
        if (caseInsensitive) {
            options = options | RegexOptions.IgnoreCase;
        }

        regex = new Regex (_searchString, options);
    }

    public override bool matches (string s)
    {
        return regex.IsMatch (s);
    }

    public override void printLineColor (string text)
    {
        Debug.WriteLine(String.Format("printLineColorRegex: {0}, length {1}", text, text.Length));

        int currentIndex = 0;
        foreach (Match match in regex.Matches(text))
        {
            Debug.WriteLine(String.Format("currentIndex : {0}, value : {1}, index : {2}", currentIndex, match.Value, match.Index));
            Debug.WriteLine(String.Format("printing ' {0}'", text.Substring (currentIndex, match.Index - currentIndex)));
            ConsoleEx.ResetColor();
            Console.Write ("{0}", text.Substring (currentIndex, match.Index - currentIndex));

            ConsoleEx.SetMatchColor();
            Debug.WriteLine(String.Format("printing ' {0}'", match.Value));
            Console.Write ("{0}",  match.Value);
            ConsoleEx.ResetColor();

            currentIndex = match.Index + match.Value.Length;
        }

        Debug.WriteLine(String.Format("currentIndex : {0}, text.Length : {1}", currentIndex, text.Length));
        if (currentIndex < text.Length)
        {
            Debug.WriteLine(String.Format("printing ' {0}'", text.Substring (currentIndex)));
            Console.Write("{0}", text.Substring (currentIndex));
        }
        Console.Write(Environment.NewLine);
    }
}

#if EXTERNALCODE
class ExternalCodeMatcher: Matcher
{
    CompilerResults results;
    Type externalMatcherType = null;
    private Matcher _o = null;

    public ExternalCodeMatcher (string file, string _searchString, bool caseInsensitive)
    {
        results = grepper.compileExternalFile(file);
        externalMatcherType = grepper.getClassFromCompilerResults(results, typeof(Matcher));

        // If the class has a constructor, we create an object
        System.Reflection.ConstructorInfo constructor = externalMatcherType.GetConstructor (new Type[] { Type.GetType("System.String"), Type.GetType("System.Boolean")});
        if (constructor != null) {
            Console.WriteLine("Found a constructor string, bool");
            _o = (Matcher) constructor.Invoke(new object[] {_searchString, caseInsensitive});
        } else {
            constructor = externalMatcherType.GetConstructor (new Type[] { Type.GetType("System.String")});
            if (constructor != null) {
                Console.WriteLine("Found a constructor, string");
                _o = (Matcher) constructor.Invoke(new object[] {_searchString});
            }
            else {
                Console.WriteLine("No constructor");
            }
        }
    }

    public override bool matches (string s)
    {
        return _o.matches(s);
    }

    public override void printLineColor (string text)
    {
        _o.printLineColor(text);
    }
}
#endif

public class InvalidXmlCharacterReplacingStreamReader : StreamReader
{
    private char replacementCharacter;	
    public InvalidXmlCharacterReplacingStreamReader(Stream stream, char replacementCharacter): base(stream)
    {
        this.replacementCharacter = replacementCharacter;
    }

    public override int Peek()
    {
        int ch = base.Peek();
        if (ch != -1)
        {
            if (
                    (ch < 0x0020 || ch > 0xD7FF) &&
                    (ch < 0xE000 || ch > 0xFFFD) &&
                    ch != 0x0009 &&
                    ch != 0x000A &&
                    ch != 0x000D
               )
            {
                return replacementCharacter;
            }
        }
        return ch;
    }

    public override int Read()
    {
        int ch = base.Read();
        if (ch != -1)
        {
            if (
                    (ch < 0x0020 || ch > 0xD7FF) &&
                    (ch < 0xE000 || ch > 0xFFFD) &&
                    ch != 0x0009 &&
                    ch != 0x000A &&
                    ch != 0x000D
               )
            {
                return replacementCharacter;
            }
        }
        return ch;
    }

    public override int Read(char[] buffer, int index, int count)
    {
        int readCount = base.Read(buffer, index, count);
        for (int i = index; i < readCount+index; i++)
        {
            char ch = buffer[i];
            if (
                    (ch < 0x0020 || ch > 0xD7FF) &&
                    (ch < 0xE000 || ch > 0xFFFD) &&
                    ch != 0x0009 &&
                    ch != 0x000A &&
                    ch != 0x000D
               )
            {
                buffer[i] = replacementCharacter;
            }
        }
        return readCount;
    }
}

public class LStreamReader : StreamReader
{
    FixedSizedQueue<string> contextQueue;
    long currentLine;
    long startAtLine;
    long stopAfterLine;

    public LStreamReader(FileStream f, long startAtLine, long stopAfterLine): base(f)
    {
        currentLine = 0;

        // Allocate the queue if required; this is required in case the line numbers
        // are referenced from the end of the file (i.e. are negative).
        if (startAtLine < 0 || stopAfterLine < 0) {
            long qsize = -Math.Min(startAtLine, stopAfterLine);
            contextQueue = new FixedSizedQueue<string>(qsize);
#if DEBUG
            Debug.WriteLine("LStreamReader: Allocating Q: {0} elements", qsize);
#endif
        }
        else {
            contextQueue = null;
        }

        // startAtLine is positive -> skip n lines
        if (startAtLine > 0) {
            while (currentLine < startAtLine - 1) {
                currentLine++;
                string s = base.ReadLine();
#if DEBUG
                Debug.WriteLine("LSreamReader: Skipping line: {0}", s);
#endif
                if (s == null) break;
            }

            // stopAfterLine negative, fill up the queue
            if (stopAfterLine < 0) {
                long numlines = -stopAfterLine;
                while (numlines-- > 0) {
                    string s = base.ReadLine();
                    if (s == null) break;
                    contextQueue.Enqueue(s);
#if DEBUG
                    Debug.WriteLine("LStreamReader: Filling Buffer line: {0}", s);
#endif
                }
            }
        }

        // startAtLine is negative -> fill the queue until file is read completely
        if (startAtLine < 0) {
            string s = base.ReadLine();
            while (s != null) {
                /* Console.WriteLine("Skipping line: {0}", s); */
                currentLine++;
                contextQueue.Enqueue(s);
                s = base.ReadLine();
            }
            currentLine -= contextQueue.Count;
        }

        this.startAtLine = startAtLine;
        this.stopAfterLine = stopAfterLine;

#if DEBUG
            Debug.WriteLine("LStreamReader: End of Setup");
            Debug.WriteLine("LStreamReader: CurrentLine at this point {0}", currentLine);
#endif
    }

    public override string ReadLine() {
        if (startAtLine == 0 && stopAfterLine == 0) {
            currentLine++;
            return base.ReadLine();
        }

        // stopAfterLine positive
        if (startAtLine > 0 && stopAfterLine > 0) {
            currentLine++;
#if DEBUG
            Debug.WriteLine("LStreamReader: Currentline = {0}, stopAfterLine = {1}", currentLine, stopAfterLine);
#endif
            if (stopAfterLine < currentLine) {
                return null;
            }
            return base.ReadLine();
        }

        // startAtLine positive stop negative. Add one line to the queue
        // and pop one line. If we reach the end of the file, we stop
        if (startAtLine > 0 && stopAfterLine < 0) {
            currentLine++;
            if (contextQueue.Count == 0)
                return null;

            string result = contextQueue.Dequeue();
            string new_line = base.ReadLine();

            if (new_line == null) {
                contextQueue.Clear();
            } else {
                contextQueue.Enqueue(new_line);
            }
            return result;
        }

        // Pop the queue until we've reached the end
        if (startAtLine < 0 && stopAfterLine > 0) {
            currentLine++;
#if DEBUG
            Debug.WriteLine("LStreamReader: Currentline = {0}, stopAfterLine = {1}", currentLine, stopAfterLine);
#endif
            if (stopAfterLine < currentLine) {
                return null;
            }
            if (contextQueue.Count > 0) {
                return contextQueue.Dequeue();
            } else {
                return null;
            }
        }

        // stopAfterLine and startAtLine both negative
        // in this case we pop the queue until their are -stopAfterLine items left.
        if (startAtLine < 0 && stopAfterLine < 0) {
            currentLine++;
#if DEBUG
            Debug.WriteLine("LStreamReader: both negative {0} {1}", currentLine, contextQueue.Count);
#endif
            if (contextQueue.Count < -stopAfterLine || contextQueue.Count > -startAtLine) {
                return null;
            } else {
                string result2 = contextQueue.Dequeue();
                /* Console.WriteLine("Returning line: {0}", result2); */
                return result2;
            }
        }
        return null;
    }

    public long CurrentLine {
        get {
            return currentLine;
        }
    }
}

public static class Util {
    private static string prevpaths = null;

    public static void PrintStackTrace(Exception e) {
        StackTrace st = new StackTrace();
        StackFrame sf = st.GetFrame(0);
        ConsoleEx.WriteErrorLine("");
        ConsoleEx.WriteErrorLine("Exception raised {0}: {1}", e, e.Message);
        ConsoleEx.WriteErrorLine("  Exception in method: ");
        ConsoleEx.WriteErrorLine("      {0}", sf.GetMethod());

        if (st.FrameCount > 1)
        {
            // Display the highest-level function call  
            // in the trace.
            sf = st.GetFrame(st.FrameCount-1);
            ConsoleEx.WriteErrorLine("  Original function call at top of call stack):");
            ConsoleEx.WriteErrorLine("      {0}", sf.GetMethod());
        }
    }

    public static string RelativePath(string path)
    {
        return RelativePath(Environment.CurrentDirectory, path);
    }

    public static string RelativePath(string absPath, string relTo)
    {
        string[] absDirs = Path.GetFullPath(absPath).Split(Path.DirectorySeparatorChar);
        string[] relDirs = Path.GetFullPath(relTo).Split(Path.DirectorySeparatorChar);

        //  --> doesn't do what I want 
        // Uri path1 = new Uri(Path.GetFullPath(absPath));
        // Uri path2 = new Uri(Path.GetFullPath(relTo));
        // Uri diff = path1.MakeRelativeUri(path2);
        // string relPath = diff.OriginalString;
        // Debug.WriteLine(string.Format("Result from URI: {0}", relPath));

        Debug.WriteLine("DirectorySeparatorChar = {0}", Path.DirectorySeparatorChar);
        Debug.WriteLine(String.Format("RelativePath: absPath {0}, Length {1}", absPath, absDirs.Length));
        Debug.WriteLine(String.Format("RelativePath: relDirs {0}, Length {1}", relTo, relDirs.Length));

        int len = absDirs.Length < relDirs.Length ? absDirs.Length : relDirs.Length;
        int lastCommonRoot = -1;
        for (int index = 0; index < len; index++)
        {
            if (absDirs[index] == relDirs[index])
                lastCommonRoot = index;
            else break;
        }

        if (lastCommonRoot == -1)
        {
            if (prevpaths == null || prevpaths != absPath) {
                ConsoleEx.WriteErrorLine("Error: Paths do not have a common base " + absPath + " & " + relTo);
                prevpaths = absPath;
            }
            return relTo;
        }

        // Build up the relative path 
        StringBuilder relativePath = new StringBuilder();
        // Add on the .. 
        for (int index = lastCommonRoot + 1; index < absDirs.Length; index++)
        {
            //Debug.WriteLine("RelativePath: appending ..");
            if (absDirs[index].Length > 0) relativePath.Append(".." + Path.DirectorySeparatorChar);
        }
        // Add on the folders 
        for (int index = lastCommonRoot + 1; index < relDirs.Length - 1; index++)
        {
            //Debug.WriteLine("RelativePath: appending " + relDirs[index]);
            relativePath.Append(relDirs[index] + Path.DirectorySeparatorChar);
        }

        //Debug.WriteLine("RelativePath: appending last relative path " + relDirs[relDirs.Length - 1]);
        relativePath.Append(relDirs[relDirs.Length - 1]);
        return relativePath.ToString();
    }

    public static string FormatFileName(string file) {
        try { 
            if (file == null) return "";
            if (file == "<stdin>") return "<stdin>";
            if (file == "") return "";

            //return Path.GetFileName(file);
            Debug.WriteLine("FormatFileName:" + file + ":" + Environment.CurrentDirectory);
            return Util.RelativePath(file);
        } catch (Exception) {
            ConsoleEx.WriteErrorLine("Error when formatting path {0}", file);
            return file;
        }
    }
}
