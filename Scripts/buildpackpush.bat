SETLOCAL
SET VERSION=%1
REM CALL Scripts\buildpack %VERSION% || exit /B 1
REM nuget push .\Artifacts\Mapsui.%VERSION%.nupkg -source nuget.org || exit /B 1
REM nuget push .\Artifacts\Mapsui.Forms.%VERSION%.nupkg -source nuget.org || exit /B 1
REM nuget push .\Artifacts\Mapsui.Native.%VERSION%.nupkg -source nuget.org || exit /B 1
git commit -m %VERSION% -a || exit /B 1
git tag %VERSION% || exit /B 1
git push origin %VERSION% || exit /B 1
git push || exit /B 1
