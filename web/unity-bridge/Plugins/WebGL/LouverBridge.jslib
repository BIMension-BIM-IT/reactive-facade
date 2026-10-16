// Unity → 웹. 이 파일은 Assets/Plugins/WebGL/ 폴더에 그대로 둔다.
mergeInto(LibraryManager.library, {
  // solar_result(JSON 문자열)를 웹 페이지로 보낸다
  LouverSendSolarResult: function (ptr) {
    var json = UTF8ToString(ptr);
    if (window.LouverBridge && window.LouverBridge.receiveSolarResult) {
      window.LouverBridge.receiveSolarResult(json);
    } else {
      console.log('[LouverBridge] solar_result', json);
    }
  },
  // Unity가 다 뜬 뒤 웹에 알린다 → 웹이 현재 건물과 시각을 보내 준다
  LouverNotifyReady: function () {
    if (window.LouverBridge && window.LouverBridge.onUnityReady) {
      window.LouverBridge.onUnityReady();
    }
  },
  // 반사광(GlareSim)이 씬에 있는지 (1 = 있음)
  LouverGlareAvailable: function (on) {
    if (window.LouverBridge && window.LouverBridge.onGlareAvailable) window.LouverBridge.onGlareAvailable(!!on);
  },
  // 반사광 계산 결과 JSON (GlareReflectionSim.onResultJson)
  LouverSendGlareResult: function (ptr) {
    var json = UTF8ToString(ptr);
    if (window.LouverBridge && window.LouverBridge.receiveGlareResult) window.LouverBridge.receiveGlareResult(json);
    else console.log('[LouverBridge] glare', json);
  }
});
