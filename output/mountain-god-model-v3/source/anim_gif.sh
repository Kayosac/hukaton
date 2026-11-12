#!/bin/bash
# usage: mkgif.sh FRAMEDIR OUTBASE
set -e
D=$1; O=$2
ffmpeg -y -loglevel error -framerate 15 -i $D/f%04d.png -vf "split[a][b];[a]palettegen=max_colors=200:stats_mode=full[p];[b][p]paletteuse=dither=sierra2_4a" -loop 0 $O.gif
ffmpeg -y -loglevel error -framerate 15 -i $D/f%04d.png -c:v libx264 -pix_fmt yuv420p -crf 20 -preset slow -vf "pad=ceil(iw/2)*2:ceil(ih/2)*2" $O.mp4
ls -la $O.gif $O.mp4
