# Introduction
A search tool for logfiles that improves on findstr.

If you are looking for a search tool, you probably want [ripgrep](https://github.com/BurntSushi/ripgrep), which
is much faster and more complete.

`lgrep` is not particularly fast, but its speed is comparable to the original `findstr`. Its redeeming features are
  - one single source code file.
  - compilable on any modern windows system without installing a huge tool chain.
  - compatible command line switches to findstr.
  - XML support.

# Installation
First clone the repository, or copy the source file lgrep.cs.


```git clone https://github.com/lroobrou/lgrep```

Then, compile it:

```C:\WINDOWS\microsoft.net\Framework64\v4.0.30319\csc.exe lgrep.cs /o```

# Usage

```
LGREP [/B] [/E] [/R] [/S] [/I] [/V] [/P] [/F:file]
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
Reference.```
