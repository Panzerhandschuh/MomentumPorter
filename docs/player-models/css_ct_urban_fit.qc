$modelname "player/spike/css_fit2.mdl"
$cdmaterials "models/player/spike/css_fit2/"
$origin 0 0 0 -90
$surfaceprop "flesh"
$includemodel "player/mom_player_animations.mdl"

$model "body" "body.smd"

$definebone "rootTransform" "" 0 0 0 -0 89.99995 0 0 0 0 0 0 0
$definebone "spine" "rootTransform" 0 0.671409 35.57312 -0 0 96.89452 0 0 0 0 0 0
$definebone "spine.001" "spine" 0 6.991604 -0 -0 0 -0.311976 0 0 0 0 0 0
$definebone "spine.002" "spine.001" -0 3.919724 0 -0 0 -6.541858 0 0 0 0 0 0
$definebone "spine.003" "spine.002" -0 5.932587 -0 -0 -0 -8.824121 0 0 0 0 0 0
$definebone "thigh.R" "spine" -2.900857 2.675976 2.521368 -9.014615 -93.6966 -3.209863 0 0 0 0 0 0
$definebone "shin.R" "thigh.R" 17.43251 0 0 7.253922 3.502427 3.752755 0 0 0 0 0 0
$definebone "foot.R" "shin.R" 16.45443 -0 0 0.377637 88.83452 124.3671 0 0 0 0 0 0
$definebone "shoulder.L" "spine.003" 0.832991 5.424461 1.102119 -75.63249 -43.67617 -52.9406 0 0 0 0 0 0
$definebone "shoulder_helper.L" "shoulder.L" -0 5.645823 -0.000008 -10.7268 -4.503511 -4.471996 0 0 0 0 0 0
$definebone "upper_arm.L" "shoulder_helper.L" 0 1.760409 0.000004 60.40448 101.2182 -160.7492 0 0 0 0 0 0
$definebone "forearm.L" "upper_arm.L" 11.38338 -0 -0.000002 1.410073 2.85746 0.457185 0 0 0 0 0 0
$definebone "hand.L" "forearm.L" 8.728874 -0 0.000002 -4.126307 -85.51395 4.845514 0 0 0 0 0 0
$definebone "thigh.L" "spine" 2.900857 2.675976 2.521368 -9.014614 -86.30336 3.209863 0 0 0 0 0 0
$definebone "shin.L" "thigh.L" 17.43252 0 0 7.253921 -3.502426 -3.752757 0 0 0 0 0 0
$definebone "foot.L" "shin.L" 16.45444 -0 0 -0.377637 91.16537 124.3671 0 0 0 0 0 0
$definebone "shoulder.R" "spine.003" -0.832992 5.424461 1.102119 75.63243 43.67623 -52.94054 0 0 0 0 0 0
$definebone "shoulder_helper.R" "shoulder.R" -0 5.645824 0 10.72657 4.503507 -4.471993 0 0 0 0 0 0
$definebone "upper_arm.R" "shoulder_helper.R" 0.000001 1.760407 -0.000004 60.40458 78.78224 160.7498 0 0 0 0 0 0
$definebone "forearm.R" "upper_arm.R" 11.38338 0 0 1.41008 -2.857514 179.5428 0 0 0 0 0 0
$definebone "hand.R" "forearm.R" 8.728876 -0 -0.000002 -4.126293 94.48601 -175.1544 0 0 0 0 0 0
$definebone "weapon_bone.R" "hand.R" -0.331387 3.227959 0.44101 -76.1353 -94.23169 -75.83539 0 0 0 0 0 0
$definebone "palm.02.L" "hand.L" 0.445426 3.018694 -1.010708 -7.916607 -16.67553 -29.82938 0 0 0 0 0 0
$definebone "f_pinky.01.L" "palm.02.L" 1.659054 1.441834 0.199261 -0.448796 159.1659 161.2521 0 0 0 0 0 0
$definebone "f_pinky.02.L" "f_pinky.01.L" -0 0.705872 0 -1.499826 -1.98123 -3.002123 0 0 0 0 0 0
$definebone "f_pinky.03.L" "f_pinky.02.L" 0 0.997562 0 1.9829 0.006535 9.635269 0 0 0 0 0 0
$definebone "f_ring.01.L" "palm.02.L" 0.807692 1.619945 0.191486 6.051509 167.4419 160.2767 0 0 0 0 0 0
$definebone "f_ring.02.L" "f_ring.01.L" -0.000001 1.013374 -0.000004 5.167452 -1.166605 -0.618045 0 0 0 0 0 0
$definebone "f_middle.01.L" "palm.02.L" -0.104961 1.597563 0.192677 -1.360364 -174.641 158.1867 0 0 0 0 0 0
$definebone "f_middle.02.L" "f_middle.01.L" -0 1.156898 0 -2.424466 -0.619429 1.684216 0 0 0 0 0 0
$definebone "f_middle.03.L" "f_middle.02.L" 0 1.488403 0.000004 4.246419 -0.25331 8.034822 0 0 0 0 0 0
$definebone "f_index.01.L" "palm.02.L" -1.005056 1.291154 0.388432 -10.41614 -156.6711 151.1504 0 0 0 0 0 0
$definebone "f_index.02.L" "f_index.01.L" 0 0.972818 0.000002 6.085384 1.481376 4.452911 0 0 0 0 0 0
$definebone "f_index.03.L" "f_index.02.L" 0.000001 1.260796 0.000002 13.06738 1.003244 10.47882 0 0 0 0 0 0
$definebone "toe.L" "foot.L" -0 4.965952 -0.000001 -1.805675 0.769138 -28.57174 0 0 0 0 0 0
$definebone "spine.004" "spine.003" -0 6.816017 0 -0.000057 -0.000115 42.59859 0 0 0 0 0 0
$definebone "spine.005" "spine.004" 0 1.181995 -0.000004 0.000229 0.000057 -17.76232 0 0 0 0 0 0
$definebone "spine.006" "spine.005" 0 1.984123 -0.000002 -0.000115 0 -15.44092 0 0 0 0 0 0
$definebone "toe.R" "foot.R" 0 4.965955 -0 1.805734 -0.769195 -28.57174 0 0 0 0 0 0
$definebone "thumb.01.L" "hand.L" -0.341505 3.17967 -0.292488 29.67032 77.91686 17.91708 0 0 0 0 0 0
$definebone "thumb.01.helper.L" "thumb.01.L" 0.000005 0.939071 0.000011 45.05813 64.23186 88.87592 0 0 0 0 0 0
$definebone "thumb.02.L" "thumb.01.helper.L" -0.000001 0.439911 -0.000002 79.25934 112.5103 124.3716 0 0 0 0 0 0
$definebone "thumb.03.L" "thumb.02.L" -0.150022 1.216583 0.160109 3.626755 11.93505 14.70089 0 0 0 0 0 0
$definebone "f_ring.03.L" "f_ring.02.L" -0.000001 1.295492 0.000002 -0.248199 -0.572335 8.810428 0 0 0 0 0 0
$definebone "thumb.01.R" "hand.R" 0.341507 3.179672 -0.292481 -29.67033 -77.91686 17.91709 0 0 0 0 0 0
$definebone "thumb.01.helper.R" "thumb.01.R" -0.000001 0.939072 0.000004 -45.05813 -64.23184 88.87592 0 0 0 0 0 0
$definebone "thumb.02.R" "thumb.01.helper.R" 0.000001 0.439911 -0.000004 -79.25927 -112.5105 124.3719 0 0 0 0 0 0
$definebone "thumb.03.R" "thumb.02.R" 0.150019 1.216576 0.160105 -3.626773 -11.93506 14.70088 0 0 0 0 0 0
$definebone "palm.02.R" "hand.R" -0.445424 3.01869 -1.010706 7.916606 16.67548 -29.82936 0 0 0 0 0 0
$definebone "f_index.01.R" "palm.02.R" 1.005056 1.291155 0.388432 10.41614 156.6711 151.1504 0 0 0 0 0 0
$definebone "f_index.02.R" "f_index.01.R" 0 0.972818 -0.000004 -6.085385 -1.481383 4.452904 0 0 0 0 0 0
$definebone "f_index.03.R" "f_index.02.R" 0 1.260801 -0.000002 -13.06739 -1.003249 10.47882 0 0 0 0 0 0
$definebone "f_middle.01.R" "palm.02.R" 0.104962 1.597565 0.192665 1.360371 174.641 158.1868 0 0 0 0 0 0
$definebone "f_middle.02.R" "f_middle.01.R" 0.000001 1.156895 -0.000006 2.424461 0.619426 1.684212 0 0 0 0 0 0
$definebone "f_middle.03.R" "f_middle.02.R" 0 1.488401 0 -4.246427 0.253306 8.03482 0 0 0 0 0 0
$definebone "f_ring.01.R" "palm.02.R" -0.807692 1.619946 0.191483 -6.051448 -167.4419 160.2767 0 0 0 0 0 0
$definebone "f_ring.02.R" "f_ring.01.R" -0 1.013374 0 -5.16746 1.166606 -0.618051 0 0 0 0 0 0
$definebone "f_ring.03.R" "f_ring.02.R" 0 1.295485 0 0.248194 0.572333 8.810425 0 0 0 0 0 0
$definebone "f_pinky.01.R" "palm.02.R" -1.659052 1.441844 0.199253 0.448802 -159.1659 161.2522 0 0 0 0 0 0
$definebone "f_pinky.02.R" "f_pinky.01.R" 0 0.705868 0.000002 1.49976 1.981237 -3.002128 0 0 0 0 0 0
$definebone "f_pinky.03.R" "f_pinky.02.R" 0 0.997559 0.000002 -1.982846 -0.006528 9.635197 0 0 0 0 0 0

$bonemerge "weapon_bone.R"
$attachment "player_forward" "spine.006" 0 3.8826 -0 rigid rotate -0 0 -0.61

$sequence "ref" "body.smd" fps 30
