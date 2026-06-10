import 'package:flutter/material.dart';
import '../theme/colors.dart';
import '../theme/typography.dart';

class NotificationBanner {
  static OverlayEntry? _currentEntry;

  static void show({
    required NavigatorState navigatorState,
    required String title,
    required String message,
    required String type,
    VoidCallback? onTap,
  }) {
    // Dismiss any active banner first
    dismiss();

    final overlay = navigatorState.overlay;
    if (overlay == null) return;

    _currentEntry = OverlayEntry(
      builder: (context) => NotificationBannerWidget(
        title: title,
        message: message,
        type: type,
        onTap: () {
          dismiss();
          if (onTap != null) onTap();
        },
        onDismiss: () => dismiss(),
      ),
    );

    overlay.insert(_currentEntry!);

    // Automatically dismiss after 4 seconds
    Future.delayed(const Duration(seconds: 4), () {
      dismiss();
    });
  }

  static void dismiss() {
    if (_currentEntry != null) {
      try {
        _currentEntry!.remove();
      } catch (_) {
        // Prevent crashes if the entry is already removed
      }
      _currentEntry = null;
    }
  }
}

class NotificationBannerWidget extends StatefulWidget {
  final String title;
  final String message;
  final String type;
  final VoidCallback onTap;
  final VoidCallback onDismiss;

  const NotificationBannerWidget({
    Key? key,
    required this.title,
    required this.message,
    required this.type,
    required this.onTap,
    required this.onDismiss,
  }) : super(key: key);

  @override
  State<NotificationBannerWidget> createState() => _NotificationBannerWidgetState();
}

class _NotificationBannerWidgetState extends State<NotificationBannerWidget>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  late final Animation<Offset> _offsetAnimation;
  late final Animation<double> _fadeAnimation;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 350),
    );

    _offsetAnimation = Tween<Offset>(
      begin: const Offset(0, -1.2),
      end: Offset.zero,
    ).animate(CurvedAnimation(
      parent: _controller,
      curve: Curves.easeOutBack,
    ));

    _fadeAnimation = Tween<double>(
      begin: 0.0,
      end: 1.0,
    ).animate(CurvedAnimation(
      parent: _controller,
      curve: Curves.easeIn,
    ));

    _controller.forward();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _dismissWithAnimation() async {
    if (_controller.isAnimating || !_controller.isCompleted) return;
    await _controller.reverse();
    widget.onDismiss();
  }

  @override
  Widget build(BuildContext context) {
    final Color sideColor;
    final IconData icon;
    final Color iconColor;

    switch (widget.type) {
      case 'Booking':
        sideColor = AppColors.primary;
        icon = Icons.calendar_today_rounded;
        iconColor = AppColors.primary;
        break;
      case 'Complaint':
        sideColor = AppColors.error;
        icon = Icons.warning_amber_rounded;
        iconColor = AppColors.error;
        break;
      case 'VIP':
        sideColor = AppColors.warning;
        icon = Icons.star_rounded;
        iconColor = AppColors.warning;
        break;
      default:
        sideColor = AppColors.secondary;
        icon = Icons.notifications_none_rounded;
        iconColor = AppColors.secondary;
    }

    final topPadding = MediaQuery.of(context).padding.top + 12;

    return SafeArea(
      child: Align(
        alignment: Alignment.topCenter,
        child: FractionallySizedBox(
          widthFactor: 0.92,
          child: SlideTransition(
            position: _offsetAnimation,
            child: FadeTransition(
              opacity: _fadeAnimation,
              child: GestureDetector(
                onTap: widget.onTap,
                onVerticalDragUpdate: (details) {
                  if (details.primaryDelta! < -5) {
                    _dismissWithAnimation();
                  }
                },
                child: Container(
                  margin: EdgeInsets.only(top: topPadding),
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                  decoration: BoxDecoration(
                    color: AppColors.surface,
                    borderRadius: BorderRadius.circular(16),
                    border: Border.all(color: AppColors.border),
                    boxShadow: [
                      BoxShadow(
                        color: Colors.black.withOpacity(0.08),
                        blurRadius: 16,
                        offset: const Offset(0, 8),
                      ),
                    ],
                  ),
                  child: Material(
                    color: Colors.transparent,
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.center,
                      children: [
                        // Left accent bar
                        Container(
                          width: 4,
                          height: 48,
                          decoration: BoxDecoration(
                            color: sideColor,
                            borderRadius: BorderRadius.circular(2),
                          ),
                        ),
                        const SizedBox(width: 12),
                        // Circular icon container
                        Container(
                          padding: const EdgeInsets.all(8),
                          decoration: BoxDecoration(
                            color: sideColor.withOpacity(0.1),
                            shape: BoxShape.circle,
                          ),
                          child: Icon(icon, color: iconColor, size: 24),
                        ),
                        const SizedBox(width: 12),
                        // Title & Body info
                        Expanded(
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            crossAxisAlignment: CrossAxisAlignment.end,
                            children: [
                              Text(
                                widget.title,
                                style: AppTypography.body.copyWith(
                                  fontWeight: FontWeight.bold,
                                  fontSize: 14,
                                  color: AppColors.text,
                                ),
                                textDirection: TextDirection.rtl,
                              ),
                              const SizedBox(height: 2),
                              Text(
                                widget.message,
                                style: AppTypography.bodyMuted.copyWith(
                                  fontSize: 12,
                                  color: AppColors.textMuted,
                                ),
                                maxLines: 2,
                                overflow: TextOverflow.ellipsis,
                                textDirection: TextDirection.rtl,
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 8),
                        // Close action
                        IconButton(
                          icon: const Icon(Icons.close_rounded, size: 18, color: AppColors.textMuted),
                          onPressed: _dismissWithAnimation,
                          constraints: const BoxConstraints(),
                          padding: EdgeInsets.zero,
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
