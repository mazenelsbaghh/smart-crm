import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile_app/core/widgets/notification_banner.dart';

void main() {
  testWidgets('important banner stays accessible at 200 percent text scaling', (
    tester,
  ) async {
    // Regression 2026-08-25: urgent banners must remain readable and
    // dismissible for screen-reader and large-text users.
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(375, 812);
    addTearDown(tester.view.resetDevicePixelRatio);
    addTearDown(tester.view.resetPhysicalSize);
    final semantics = tester.ensureSemantics();
    var dismissed = false;

    await tester.pumpWidget(
      MaterialApp(
        home: MediaQuery(
          data: MediaQueryData.fromView(tester.view).copyWith(
            textScaler: const TextScaler.linear(2),
            disableAnimations: true,
          ),
          child: Directionality(
            textDirection: TextDirection.rtl,
            child: Scaffold(
              body: NotificationBannerWidget(
                title: 'تنبيه مهم يحتاج إلى انتباهك الآن',
                message:
                    'تعذر إكمال الإجراء. راجع البيانات وحاول مرة أخرى من فضلك.',
                type: 'Complaint',
                onTap: null,
                onDismiss: () => dismissed = true,
              ),
            ),
          ),
        ),
      ),
    );

    expect(tester.takeException(), isNull);
    final liveRegion = find.byWidgetPredicate(
      (widget) => widget is Semantics && widget.properties.liveRegion == true,
    );
    expect(liveRegion, findsOneWidget);
    expect(tester.getSemantics(liveRegion), isSemantics(isLiveRegion: true));

    final closeButton = find.byTooltip('إغلاق التنبيه');
    expect(closeButton, findsOneWidget);
    expect(
      tester.getSemantics(closeButton),
      isSemantics(
        tooltip: 'إغلاق التنبيه',
        isButton: true,
        isEnabled: true,
        hasEnabledState: true,
        hasTapAction: true,
      ),
    );
    await tester.tap(closeButton);
    await tester.pump();

    expect(dismissed, isTrue);
    expect(tester.takeException(), isNull);
    semantics.dispose();
  });
}
