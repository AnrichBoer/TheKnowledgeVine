FROM mcr.microsoft.com/dotnet/aspnet:10.0.13
WORKDIR /app
COPY publish/ .
ENV ASPNETCORE_URLS=http://+:8082
EXPOSE 8082
USER $APP_UID
ENTRYPOINT ["dotnet", "KnowledgeVineAPI.dll"]
