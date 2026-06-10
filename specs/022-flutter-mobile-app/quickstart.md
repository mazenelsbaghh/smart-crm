# Developer Quickstart: Flutter App

This guide outlines how to run, configure, and test the Flutter mobile application.

## 1. Prerequisites

Ensure you have the following installed on your development machine:
- Flutter SDK (v3.41.0 or higher)
- Dart SDK (v3.11.0 or higher)
- Android Studio / Xcode (for emulators and SDK tools)

## 2. Configuration Setup

Copy `.env.example` file or configure your local environment settings inside the app configurations:
- Backend base URL: `http://localhost:5000` (or `http://10.0.2.2:5000` for Android emulator loopback).
- WebSocket (SignalR) URL: `ws://localhost:5000/hubs` (or `ws://10.0.2.2:5000/hubs` for Android emulator).

## 3. Installation

Run the following commands inside the `mobile_app` folder to fetch dependencies:

```bash
flutter pub get
```

## 4. Run the Application

To run the application locally on a connected emulator or physical device:

```bash
flutter run
```

## 5. Running Tests

### Unit & Widget Tests
Run all unit and widget tests using the Flutter test runner:

```bash
flutter test
```

### Coverage Reports
To generate a test coverage report:

```bash
flutter test --coverage
```
