FROM public.ecr.aws/amazonlinux/amazonlinux:2.0.20260202.2-arm64v8
WORKDIR /bin
COPY bin/Release/net8.0/linux-arm64/* .

EXPOSE 8720/udp
ENTRYPOINT [ "./MeadowIndividualServer" "gamelift=true"]