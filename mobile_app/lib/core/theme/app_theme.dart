import 'package:flutter/material.dart';

import 'colors.dart';
import 'typography.dart';

class AppTheme {
  AppTheme._();

  static ThemeData get dark {
    final colorScheme = const ColorScheme.dark(
      primary: AppColors.primary,
      onPrimary: AppColors.background,
      primaryContainer: AppColors.primaryContainer,
      onPrimaryContainer: AppColors.text,
      secondary: AppColors.secondary,
      onSecondary: AppColors.background,
      secondaryContainer: AppColors.secondaryContainer,
      onSecondaryContainer: AppColors.text,
      error: AppColors.error,
      onError: AppColors.background,
      surface: AppColors.surface,
      onSurface: AppColors.text,
      outline: AppColors.border,
    );

    final inputBorder = OutlineInputBorder(
      borderRadius: BorderRadius.circular(8),
      borderSide: const BorderSide(color: AppColors.border),
    );

    return ThemeData(
      useMaterial3: true,
      brightness: Brightness.dark,
      colorScheme: colorScheme,
      scaffoldBackgroundColor: AppColors.background,
      canvasColor: AppColors.surface,
      dividerColor: AppColors.border,
      splashFactory: InkRipple.splashFactory,
      visualDensity: VisualDensity.standard,
      textTheme: TextTheme(
        displaySmall: AppTypography.display,
        headlineSmall: AppTypography.headline,
        titleMedium: AppTypography.title,
        bodyMedium: AppTypography.body,
        bodySmall: AppTypography.bodyMuted,
        labelMedium: AppTypography.label,
      ),
      appBarTheme: const AppBarTheme(
        backgroundColor: AppColors.surface,
        foregroundColor: AppColors.text,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        centerTitle: true,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: AppColors.surface,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16,
          vertical: 14,
        ),
        border: inputBorder,
        enabledBorder: inputBorder,
        focusedBorder: inputBorder.copyWith(
          borderSide: const BorderSide(color: AppColors.primary, width: 2),
        ),
        errorBorder: inputBorder.copyWith(
          borderSide: const BorderSide(color: AppColors.error),
        ),
        focusedErrorBorder: inputBorder.copyWith(
          borderSide: const BorderSide(color: AppColors.error, width: 2),
        ),
        hintStyle: AppTypography.bodyMuted,
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          minimumSize: const Size(48, 48),
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
          backgroundColor: AppColors.primary,
          foregroundColor: AppColors.background,
          disabledBackgroundColor: AppColors.border,
          disabledForegroundColor: AppColors.textMuted,
          elevation: 0,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(48, 48),
          foregroundColor: AppColors.text,
          side: const BorderSide(color: AppColors.border),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          minimumSize: const Size(48, 48),
          foregroundColor: AppColors.primary,
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(
          minimumSize: const Size.square(48),
          foregroundColor: AppColors.text,
        ),
      ),
      chipTheme: ChipThemeData(
        backgroundColor: AppColors.surfaceRaised,
        selectedColor: AppColors.primary,
        side: const BorderSide(color: AppColors.border),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(22)),
      ),
      bottomNavigationBarTheme: const BottomNavigationBarThemeData(
        backgroundColor: AppColors.surface,
        selectedItemColor: AppColors.primary,
        unselectedItemColor: AppColors.textMuted,
        type: BottomNavigationBarType.fixed,
        elevation: 0,
      ),
      snackBarTheme: SnackBarThemeData(
        backgroundColor: AppColors.surfaceRaised,
        contentTextStyle: AppTypography.body,
        behavior: SnackBarBehavior.floating,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
      ),
      tooltipTheme: TooltipThemeData(
        decoration: BoxDecoration(
          color: AppColors.surfaceRaised,
          border: Border.all(color: AppColors.border),
          borderRadius: BorderRadius.circular(4),
        ),
        textStyle: AppTypography.label.copyWith(color: AppColors.text),
        waitDuration: const Duration(milliseconds: 350),
      ),
      progressIndicatorTheme: const ProgressIndicatorThemeData(
        color: AppColors.primary,
        linearTrackColor: AppColors.border,
      ),
      focusColor: AppColors.primaryContainer,
    );
  }
}
