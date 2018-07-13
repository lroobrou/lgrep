lgrep.exe: lgrep.cs
	mcs lgrep.cs -debug -define:DEBUG

release:
	mcs lgrep.cs -optimize

test: Tests.dll 
	cd unittest ; nunit-console4 ../Tests.dll ; cd ..

Tests.dll: TestClass.cs lgrep.exe
	mcs -target:library -r:lgrep.exe -r:nunit.framework -out:Tests.dll TestClass.cs -debug

