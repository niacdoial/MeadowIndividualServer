FROM public.ecr.aws/amazonlinux/amazonlinux:2023-minimal
RUN sudo yum install libsodium -y
RUN sudo yum install libicu -y

WORKDIR /bin
COPY bin/Release/net8.0/linux-arm64/* .
EXPOSE 8720/udp
ENTRYPOINT [ "./MeadowIndividualServer" "gamelift=true" ]