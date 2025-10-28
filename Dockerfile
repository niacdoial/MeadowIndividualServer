FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /bin
COPY bin/Release/net8.0/linux-x64/* .

EXPOSE 8720
ENTRYPOINT [ "./MeadowIndividualServer" ]