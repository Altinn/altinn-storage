FROM mcr.microsoft.com/dotnet/sdk:10.0.404-resolute@sha256:f685dd6b5a970778b53cead9a11cf54206c4b4fc3c97e4424a59b8bff9aa43d7 AS build

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
FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-resolute-chiseled-extra@sha256:728d90568c65ec25adf8bacd9da65356a7f5bbeb37eaca1193e6ed082900a1bc AS final
EXPOSE 5010
WORKDIR /app
COPY --from=build /app_output .
COPY --from=build /Storage/Migration ./Migration
USER $APP_UID

ENTRYPOINT ["dotnet", "Altinn.Platform.Storage.dll"]
