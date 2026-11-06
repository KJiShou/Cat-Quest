mergeInto(LibraryManager.library, {
  CatQuest_ReadSave__deps: ['$stringToNewUTF8'],
  CatQuest_ReadSave: function () {
    var result;
    try {
      result = { value: localStorage.getItem('catquest.player.v1') || '' };
    } catch (error) {
      result = { error: 'Browser storage is blocked. Allow site storage and reload to save progress.' };
    }
    return stringToNewUTF8(JSON.stringify(result));
  },
  CatQuest_WriteSave__deps: ['$stringToNewUTF8'],
  CatQuest_WriteSave: function (expectedPointer, valuePointer) {
    try {
      var key = 'catquest.player.v1';
      if ((localStorage.getItem(key) || '') !== UTF8ToString(expectedPointer))
        return stringToNewUTF8('This save changed in another tab. Close other Cat Quest tabs and reload before continuing.');
      localStorage.setItem(key, UTF8ToString(valuePointer));
      return 0;
    } catch (error) {
      return stringToNewUTF8('Browser storage is full or blocked. Your change was not saved. Export a backup and free site storage before retrying.');
    }
  },
  CatQuest_DownloadBackup__deps: ['$stringToNewUTF8'],
  CatQuest_DownloadBackup: function (jsonPointer) {
    try {
      var url = URL.createObjectURL(new Blob([UTF8ToString(jsonPointer)], { type: 'application/json' }));
      var link = document.createElement('a');
      link.href = url;
      link.download = 'Cat Quest Backup.json';
      document.body.appendChild(link);
      link.click();
      link.remove();
      setTimeout(function () { URL.revokeObjectURL(url); }, 10000);
      return 0;
    } catch (error) {
      return stringToNewUTF8('The browser could not download your backup.');
    }
  }
});
