param([string]$Configuration = "Release", [string]$Runtime = "win-x64")
dotnet restore 4rVivi.sln
dotnet build 4rVivi.sln -c $Configuration --no-restore
dotnet publish src/4rVivi.App/4rVivi.App.csproj -c $Configuration -r $Runtime --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/$Runtime
