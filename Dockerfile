FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim
WORKDIR /app

# copy csproj
COPY *.csproj ./
# copy code
COPY *.cs ./
# show files
RUN ls
# Restore 
RUN dotnet restore
# build
RUN dotnet publish -c Release -o out
# run
CMD ASPNETCORE_URLS=http://*:$PORT dotnet out/SLB.dll $DISCORDTOKEN $STEAMUSER $STEAMPASS $MESSAGEWAIT $MESSAGECOUNT