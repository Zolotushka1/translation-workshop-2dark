# Third-party material

## 2Dark

2Dark and its original content belong to their respective rights holders. This is an unofficial tool, not a distribution of the game. No original EXE, DLL, bigfile archive, audio, or extracted original language corpus is included. Setup extracts the required data locally.

## PG Team Russian localization

The legacy Russian translation is credited to **PG Team**. Source listing: <https://www.zoneofgames.ru/games/2dark/files/5785.html>.

Included unchanged from the locally extracted legacy package:

- `old_extracted/`: 13 Russian text tables in the English language slot, including Office variants, and three Cyrillic font descriptors.
- `old_payload/`: three Cyrillic font PNG atlases plus the translated `cannibal_final.png`, `livre_chorbith.png`, and `Radio_Crane.png`.

The workshop displays legacy text as reference and copies individual selected entries on request. Legacy fonts can be enabled for testing. The three translated images are reference assets and are not automatically installed by the current builder.

These files retain third-party authorship and are excluded from the workshop's MIT license. Inclusion does not grant an additional redistribution license. The reviewed package did not establish an explicit redistribution license; that status remains unresolved. See `LICENSE_SCOPE.md` for the scope of the software license.

## QuickBMS extraction script

`sources/2dark_extended.bms` derives from the 2Dark script distributed through Luigi Auriemma's QuickBMS resources. Its original header credits spider91. The local modification accepts the 0x46 QuickLZ frame used by rebuilt archives. Original comments are retained.

QuickBMS itself is not bundled. Obtain it from <https://aluigi.altervista.org/quickbms.htm> and consult its distribution terms.
