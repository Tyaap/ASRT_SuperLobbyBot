FROM mcr.microsoft.com/dotnet/sdk:6.0-bullseye-slim
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
ENTRYPOINT ["dotnet", "out/SLB.dll"]