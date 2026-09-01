import 'dart:async';
import 'dart:collection';

import 'package:flutter/material.dart';

import '../theme/colors.dart';
import '../theme/typography.dart';

class NotificationBanner {
  static OverlayEntry? _currentEntry;
  static Timer? _dismissTimer;
  static final Queue<_BannerRequest> _pending = Queue<_BannerRequest>();

  static void show({
    required NavigatorState navigatorState,
    required String title,
    required String message,
    required String type,
    VoidCallback? onTap,
  }) {
    _pending.add(
      _BannerRequest(
        navigatorState: navigatorState,
        title: title,
        message: message,
        type: type,
        onTap: onTap,
      ),
    );
    _showNext();
  }

  static void _showNext() {
    if (_currentEntry != null || _pending.isEmpty) return;
    final request = _pending.first;
    if (!request.navigatorState.mounted) {
      _pending.removeFirst();
      scheduleMicrotask(_showNext);
      return;
    }

    final overlay = request.navigatorState.overlay;
    if (overlay == null) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _showNext());
      return;
    }
    _pending.removeFirst();

    late final OverlayEntry entry;
    entry = OverlayEntry(
      builder: (context) => NotificationBannerWidget(
        title: request.title,
        message: request.message,
        type: request.type,
        onTap: request.onTap == null
            ? null
            : () {
                dismiss();
                request.onTap!();
              },
        onDismiss: () => dismiss(),
      ),
    );
    _currentEntry = entry;
    overlay.insert(entry);

    final mediaQuery = MediaQuery.maybeOf(request.navigatorState.context);
    if (mediaQuery?.accessibleNavigation != true) {
      _dismissTimer = Timer(const Duration(seconds: 8), () {
        if (identical(_currentEntry, entry)) dismiss();
      });
    }
  }

  static void dismiss() {
    _dismissTimer?.cancel();
    _dismissTimer = null;
    final entry = _currentEntry;
    _currentEntry = null;
    if (entry != null) {
      try {
        entry.remove();
      } catch (_) {
        // The overlay may already have been disposed during navigation.
      }
    }
    scheduleMicrotask(_showNext);
  }

  static void clear() {
    _dismissTimer?.cancel();
    _dismissTimer = null;
    _pending.clear();
    final entry = _currentEntry;
    _currentEntry = null;
    if (entry != null) {
      try {
        entry.remove();
      } catch (_) {
        // Navigation may already have disposed the owning overlay.
      }
    }
  }
}

class _BannerRequest {
  const _BannerRequest({
    required this.navigatorState,
    required this.title,
    required this.message,
    required this.type,
    this.onTap,
  });

  final NavigatorState navigatorState;
  final String title;
  final String message;
  final String type;
  final VoidCallback? onTap;
}

class NotificationBannerWidget extends StatefulWidget {
  const NotificationBannerWidget({
    super.key,
    required this.title,
    required this.message,
    required this.type,
    required this.onTap,
    required this.onDismiss,
  });

  final String title;
  final String message;
  final String type;
  final VoidCallback? onTap;
  final VoidCallback onDismiss;

  @override
  State<NotificationBannerWidget> createState() =>
      _NotificationBannerWidgetState();
}

class _NotificationBannerWidgetState extends State<NotificationBannerWidget>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  late final Animation<Offset> _offsetAnimation;
  late final Animation<double> _fadeAnimation;
  final _dismissibleKey = UniqueKey();

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 220),
    );

    _offsetAnimation = Tween<Offset>(
      begin: const Offset(0, -1.2),
      end: Offset.zero,
    ).animate(CurvedAnimation(parent: _controller, curve: Curves.easeOutCubic));

    _fadeAnimation = Tween<double>(
      begin: 0.0,
      end: 1.0,
    ).animate(CurvedAnimation(parent: _controller, curve: Curves.easeOut));
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.disableAnimationsOf(context)) {
      _controller.value = 1;
    } else if (!_controller.isCompleted && !_controller.isAnimating) {
      _controller.forward();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _dismissWithAnimation() async {
    if (MediaQuery.disableAnimationsOf(context)) {
      widget.onDismiss();
      return;
    }
    if (!_controller.isCompleted) {
      widget.onDismiss();
      return;
    }
    await _controller.reverse();
    if (!mounted) return;
    widget.onDismiss();
  }

  @override
  Widget build(BuildContext context) {
    final Color stateColor;
    final IconData icon;

    switch (widget.type) {
      case 'Booking':
        stateColor = AppColors.primary;
        icon = Icons.calendar_today_rounded;
        break;
      case 'Complaint':
        stateColor = AppColors.error;
        icon = Icons.warning_amber_rounded;
        break;
      case 'VIP':
        stateColor = AppColors.warning;
        icon = Icons.star_rounded;
        break;
      default:
        stateColor = AppColors.secondary;
        icon = Icons.notifications_none_rounded;
    }

    final banner = Semantics(
      container: true,
      liveRegion: true,
      explicitChildNodes: true,
      child: Dismissible(
        key: _dismissibleKey,
        direction: DismissDirection.up,
        onDismissed: (_) => widget.onDismiss(),
        child: Material(
          color: AppColors.surface,
          shape: RoundedRectangleBorder(
            side: const BorderSide(color: AppColors.border),
            borderRadius: BorderRadius.circular(16),
          ),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            onTap: widget.onTap,
            child: Padding(
              padding: const EdgeInsetsDirectional.fromSTEB(16, 10, 8, 10),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.center,
                children: [
                  Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      color: stateColor.withValues(alpha: 0.12),
                      shape: BoxShape.circle,
                    ),
                    alignment: Alignment.center,
                    child: Icon(icon, color: stateColor, size: 24),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          widget.title,
                          style: AppTypography.body.copyWith(
                            fontWeight: FontWeight.bold,
                            color: AppColors.text,
                          ),
                        ),
                        if (widget.message.isNotEmpty) ...[
                          const SizedBox(height: 2),
                          Text(
                            widget.message,
                            style: AppTypography.bodyMuted,
                            maxLines: 3,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(width: 4),
                  IconButton(
                    tooltip: 'إغلاق التنبيه',
                    icon: const Icon(Icons.close_rounded),
                    color: AppColors.textMuted,
                    onPressed: _dismissWithAnimation,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );

    final animatedBanner = MediaQuery.disableAnimationsOf(context)
        ? banner
        : SlideTransition(
            position: _offsetAnimation,
            child: FadeTransition(opacity: _fadeAnimation, child: banner),
          );

    return SafeArea(
      child: Align(
        alignment: Alignment.topCenter,
        child: Padding(
          padding: const EdgeInsetsDirectional.fromSTEB(8, 12, 8, 0),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 640),
            child: SizedBox(width: double.infinity, child: animatedBanner),
          ),
        ),
      ),
    );
  }
}
