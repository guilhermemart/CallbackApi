FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/CallbackApi/CallbackApi.csproj", "src/CallbackApi/"]
RUN dotnet restore "src/CallbackApi/CallbackApi.csproj"

COPY src/CallbackApi/ src/CallbackApi/
WORKDIR /src/src/CallbackApi
RUN dotnet publish "CallbackApi.csproj" --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
COPY --chmod=755 docker/callBackApi/entrypoint.sh /app/entrypoint.sh

ENTRYPOINT ["/app/entrypoint.sh"]
CMD ["dotnet", "CallbackApi.dll"]
