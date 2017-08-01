# ProtocolReader
ProtocolReader class reads bytes according to a certain protocol.

![ProtocolReader](https://raw.githubusercontent.com/matarillo/matarillo.github.io/master/ProtocolReader/image1.png)

## Installation

run the following command in [NuGet Package Manager Console](https://docs.microsoft.com/ja-jp/nuget/tools/package-manager-console).

```
PM> Install-Package Matarillo.IO
```

## Usage

```cs
public async Task<byte[]> ReadToSeparatorAsync(byte[] separator)
```

Reads bytes from the current stream into a byte array until the specified separator occurs, and advances the current position to the next of the separator. The byte array that is returned does not contain the separator.

```cs
public async Task<byte[]> ReadBytesAsync(int count)
```

Reads the specified number of bytes from the current stream into a byte array and advances the current position by that number of bytes.
