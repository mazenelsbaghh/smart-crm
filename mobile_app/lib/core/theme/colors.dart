import 'package:flutter/material.dart';

class AppColors {
  AppColors._();

  // Primary Cyber Accents
  static const Color primary = Color(0xFF00F3FF); // Vibrant Cyber Cyan
  static const Color secondary = Color(0xFFFF007F); // Vibrant Hot Pink

  // Neutrals (Space Navy Tinted)
  static const Color background = Color(0xFF0A0E17); // Midnight Space Background
  static const Color surface = Color(0xFF121824); // Slate Slate Surface
  static const Color border = Color(0xFF1E293B); // Dark Slate Border
  static const Color text = Color(0xFFF8FAFC); // Slate White
  static const Color textMuted = Color(0xFF94A3B8); // Slate Muted

  // Semantic Statuses
  static const Color success = Color(0xFF10B981); // Emerald Success
  static const Color warning = Color(0xFFF59E0B); // Amber Warning
  static const Color error = Color(0xFFEF4444); // Vibrant Red Error

  // Glow Helpers
  static BoxShadow neonGlow({Color color = primary}) {
    return BoxShadow(
      color: color.withOpacity(0.25),
      blurRadius: 15,
      spreadRadius: 1,
    );
  }
}
