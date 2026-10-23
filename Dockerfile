FROM mcr.microsoft.com/dotnet/sdk:10.0.401-resolute@sha256:d8051d88928fb40f74c9aca6896a57851cdced277f4102e1c5aa6d4983e27bad AS build

COPY src/Storage ./Storage
COPY src/DbTools ./DbTools
COPY src/Storage/Migration ./Migration

WORKDIR DbTools/
RUN dotnet build ./DbTools.csproj -c Release -o /app_tools

# Comment in the following line for local development
# RUN mkdir -p /DbTools/bin/Debug/net10.0 && cp /app_tools/DbTools /DbTools/bin/Debug/net10.0/DbTools

WORKDIR ../Storage/

RUN dotnet publish ./Altinn.Platform.Storage.csproj -c Release -o /app_output

# -extra includes ICU and tzdata, needed for non-English cultures such as "no-NB"
FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-resolute-chiseled-extra@sha256:760fdfff2cf3662d9474720146becafd91cd66eee98cba40e1c0f0a3c5eda491 AS final
EXPOSE 5010
WORKDIR /app
COPY --from=build /app_output .
COPY --from=build /Storage/Migration ./Migration
USER $APP_UID

ENTRYPOINT ["dotnet", "Altinn.Platform.Storage.dll"]
