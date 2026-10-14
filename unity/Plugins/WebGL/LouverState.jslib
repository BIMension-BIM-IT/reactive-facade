// 유니티 -> 웹: 화면 버튼으로 바뀐 상태(날짜·시각·대안·방향·겨울 모드)를 웹에 알려요.
// 웹 쪽에서 window.LouverBridge.receiveState = function (state) { ... } 를 만들어 두면 불려요.
mergeInto(LibraryManager.library, {
  LouverStateChanged: function (jsonPtr) {
    var json = UTF8ToString(jsonPtr);
    try {
      if (typeof window !== 'undefined' && window.LouverBridge && typeof window.LouverBridge.receiveState === 'function') {
        window.LouverBridge.receiveState(JSON.parse(json));
      }
    } catch (e) {
      console.error('LouverBridge.receiveState 오류', e);
    }
  }
});
