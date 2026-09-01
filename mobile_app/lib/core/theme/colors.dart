import 'package:flutter/material.dart';

class AppColors {
  AppColors._();

  // Restrained "Neon Midnight" palette. Accents are reserved for focus,
  // selection and semantic state, while the working canvas stays restful.
  static const Color primary = Color(0xFF00D9E6);
  static const Color primaryContainer = Color(0xFF073A42);
  static const Color secondary = Color(0xFFFF5CA8);
  static const Color secondaryContainer = Color(0xFF4A1633);

  static const Color background = Color(0xFF0A0E17);
  static const Color surface = Color(0xFF121824);
  static const Color surfaceRaised = Color(0xFF182131);
  static const Color border = Color(0xFF2A3850);
  static const Color text = Color(0xFFF1F5F9);
  static const Color textMuted = Color(0xFFB0BED0);

  static const Color success = Color(0xFF34D399);
  static const Color warning = Color(0xFFFBBF24);
  static const Color error = Color(0xFFFF6B78);

  // Glow Helpers
  static BoxShadow neonGlow({Color color = primary}) {
    return BoxShadow(
      color: color.withValues(alpha: 0.22),
      blurRadius: 15,
      spreadRadius: 1,
    );
  }
}
