# .NET Core Websocket Server

A small .NET Core Websocket Server Testproject.

#### To start use:

```
dotnet run -- /h				// shows help
dotnet run -- /p 9000			// start server @ Port 9000
```

#### Build with following command
```
dotnet build					// standard build (dll)
dotnet build -v q 				// quiet mode
dotnet build -v d 				// debug mode
dotnet build -r win10-x64 		// windows x64 build (websocket.exe)
```

#### Restore Packages
```
dotnet restore
```

