import 'package:flutter/material.dart';

class AppColors {
  AppColors._();

  // Primary Cyber Accents
  static const Color primary = Color(0xFF00838F); // Professional Dark Teal/Cyan for Light Mode readability
  static const Color secondary = Color(0xFFD81B60); // Vibrant Magenta/Pink for details

  // Neutrals (Slate Light Theme)
  static const Color background = Color(0xFFF8FAFC); // Clean Light Slate Background
  static const Color surface = Color(0xFFFFFFFF); // Pure White Surface
  static const Color border = Color(0xFFE2E8F0); // Slate 200 Border
  static const Color text = Color(0xFF0F172A); // Slate 900 Text
  static const Color textMuted = Color(0xFF64748B); // Slate 500 Muted Text

  // Semantic Statuses
  static const Color success = Color(0xFF059669); // Green Success
  static const Color warning = Color(0xFFD97706); // Amber Warning
  static const Color error = Color(0xFFDC2626); // Red Error

  // Glow Helpers
  static BoxShadow neonGlow({Color color = primary}) {
    return BoxShadow(
      color: color.withOpacity(0.25),
      blurRadius: 15,
      spreadRadius: 1,
    );
  }
}
