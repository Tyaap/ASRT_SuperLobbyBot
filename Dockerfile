FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster
WORKDIR /app

# copy csproj
COPY *.csproj ./
# copy code
COPY *.cs ./
# copy nuget.config
COPY *.config ./
# copy local packages
COPY packages ./packages
# show files
RUN ls
# Restore 
RUN dotnet restore
# build
RUN dotnet publish -c Release -o out
# run
CMD ASPNETCORE_URLS=http://*:$PORT dotnet out/SLB.dll $DISCORDTOKEN $STEAMUSER $STEAMPASS