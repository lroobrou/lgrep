lgrep.exe: lgrep.cs
	mcs lgrep.cs -debug -define:DEBUG
release:
	mcs lgrep.cs -optimize
