FROM mcr.microsoft.com/dotnet/sdk:8.0.404-alpine3.20 AS build

COPY src/Storage ./Storage
COPY src/DbTools ./DbTools
COPY src/Storage/Migration ./Migration

WORKDIR DbTools/
RUN dotnet build ./DbTools.csproj -c Release -o /app_tools

WORKDIR ../Storage/

RUN dotnet build ./Altinn.Platform.Storage.csproj -c Release -o /app_output
RUN dotnet publish ./Altinn.Platform.Storage.csproj -c Release -o /app_output

FROM mcr.microsoft.com/dotnet/aspnet:8.0.11-alpine3.20 AS final
EXPOSE 5010

#COPY ./my-custom-font.ttf ./
#RUN mkdir -p /usr/share/fonts/truetype/
#RUN install -m644 my-custom-font.ttf /usr/share/fonts/truetype/
#RUN rm ./my-custom-font.ttf

RUN apk --update --upgrade --no-cache add fontconfig ttf-freefont font-noto terminus-font font-awesome \ 
   && fc-cache -f \ 
   && fc-list | sort

WORKDIR /app
COPY --from=build /app_output .
COPY --from=build /Storage/Migration ./Migration

# setup the user and group
# the user will have no password, using shell /bin/false and using the group dotnet
RUN addgroup -g 3000 dotnet && adduser -u 1000 -G dotnet -D -s /bin/false dotnet
# update permissions of files if neccessary before becoming dotnet user
USER dotnet
RUN mkdir /tmp/logtelemetry

ENTRYPOINT ["dotnet", "Altinn.Platform.Storage.dll"]
