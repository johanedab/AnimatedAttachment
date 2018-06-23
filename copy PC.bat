set dev=D:\Dropbox (EDAB)\Private\KSP\VC\AnimatedAttachment
set bin=%dev%\AnimatedAttachment\bin\Debug
set moddev=%dev%\Gamedata\AnimatedAttachment

set gamedata=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program Dev\GameData
set mod=%gamedata%\AnimatedAttachment

copy "%bin%\AnimatedAttachment.dll" "%moddev%\Plugins\"
copy "%moddev%\*.*" "%mod%\"
copy "%moddev%\Plugins\*.*" "%mod%\Plugins\"

pause
