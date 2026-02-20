

build:
	dotnet publish --self-contained --runtime linux-arm64 -o bin -c Release

container: build
	docker buildx build . -t meadow-server --load --platform=linux/arm64

.PHONY: build, container