

build:
	dotnet publish --self-contained --runtime linux-arm64 -o bin -c Release

container: build
	docker buildx build . --platform linux/arm64 -t rainmeadow-server

.PHONY: build, container