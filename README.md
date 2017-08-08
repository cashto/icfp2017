# ICFP 2017

## Overview

The contest problem was to write a program which plays a sort of railroad-building game on an arbitrary graph against programs written by other teams.  In keeping with the ICFP contest's long tradition of goofy and sometimes downright bizarre themes, the railroads are actually called rivers, train stations are called "lambda mines", and trains are float-bottom boats called *punts* (which are apparently common in England, the land of this year's contest organizers).

Points are awarded quadratically according to the minimum distance between a lambda mine and the site. Key to getting a good score is being able to create long chains of connected rivers, and preventing other teams from doing the same.

[Here is a visualization](https://cashto.github.io/icfp2017/visualizer.html) of my lightning vs standard entries on several different maps.

## Lightning Submission

> *"Funken sprühen, die Schweißnaht glüht*
>
> *Ich bau mir einen Prototyp"*

Every AI deserves a name; I christened this year's entry as '[Prototyp](https://www.youtube.com/watch?v=75qTY9biW2k)'.

Given the potentially large branching factor, the presence of multiple opponents on the same map, and the hard requirement that every move must be made in under one second -- it seemed pretty clear to me that minimax or alpha-beta search was simply not feasible. As such, Prototyp only looks one ply ahead, not even considering its opponent's potential responses.

Prototyp's lightning heuristic is basically just a greedy algorithm (pick the river that immediately scores the most points) -- however, the first 10% of moves are played using an alternative heuristic.  A "liberty" metric is defined as the sum of the minimum distance from each mine to every site on the map (along unclaimed rivers or rivers claimed by an opponent).  If a site is unreachable, its minimum distance is 200.  Higher values of "liberty" are better, as they indicate that overall, it is harder for an opponent to reach distant points from a given mine (and consequently, easier for us).  

This usually does a decent job of finding choke points on a map, although occasionally it leads Prototyp to claim some completely fringe point, connected to the rest of the map by a single river, in order to deny the possibility of an opponent of ever forming a long chain that includes that distant point.

This alternative heuristic is only used on maps with less than 400 sites (otherwise, computing the metric takes too long).

The lightning submission contained a pretty severe bug due to my misreading of the spec: I was under the impression that the entire move list for the whole game was sent in each move message from the server -- but in reality, only the delta from the previous move message is sent.  If you want the entire move history for the whole game, you need to keep it yourself.  As a result, Prototyp suffers from severe anterograde amnesia and likely plays the same moves over and over again.

## Standard Submission

The Prototyp algorithm for the standard round is based around the idea of connecting "trees", where a tree is defined as a set of sites connected by rivers.  At the beginning of the game, each mine is considered a single-point tree.

Prototyp then considers **threats** towards connecting trees.  For each pair of trees, the minimum-distance path between them along unclaimed rivers is calculated.  Then Prototyp tests each river along that path to determine if it is a "choke" -- that is, if removing it makes one tree unreachable from the other, or at least increases the minimum-distance path by at least 30%.  If any chokes are found, then the best one is played.

Otherwise, each river (containing at least one point in an existing tree) is considered. Each potential move is evaluated against three metrics:

* Total tree count: if the tree count decreases, the move joins two trees and will always be preferred over a move that doesn't.
* Neighboring site count: if there is an increase in the number of sites reachable by up to two rivers from a tree, this means Prototyp will face an expanded selection of moves in its next turn, increasing the possibility to make a good one.
* Total score: all else being equal, Prototyp will pick the move that scores the most points.

These two searches are actually performed in parallel, taking advantage of both cores in judging environment. Choke-finding generally takes 4-5 times longer than regular move finding.  In order to guarantee a response is sent before the timeout, both searches are programmed to terminate and return the best solution after 900 milliseconds. 

Prototyp will also consider threats towards connecting trees from the opponent's point of view -- that is, to try to make moves that serve no other purpose than preventing the opponent from joining trees. About seven hours before the end of the contest, for some poor reason, [I decided to switch the priority](https://github.com/cashto/icfp2017/commit/4d423c2e17e19f2da83e22f6aa9b8717aa8bdfb4) to prefer making these sorts of moves over making a move that claims a choke for myself. The disasterous consequences of this decision were eventually noticed about four hours later in some online testing against multiple opponents, but [the fix](https://github.com/cashto/icfp2017/commit/bb75e9d04bdacc24c26ebc23ed440095d8c264c9) was to disable disruptive moves in games with more than two players. Hopefully the contest will be judged more with battle-royale contests and not head-to-head matchups.

## Extensions

Similar to the 2012 contest, various extensions to the game rules were introduced during the contest:

I elected not to implement the Futures extension, since it seemed that the number of points up for grabs was not very large, and a better return on investment could be made by instead focusing on the core AI. Also, there was the potential downside of losing points if a Future could not be completed. The code to choose Futures seemed complex, hard to test, and in the end I did not feel confident in my ability to pick Futures with greater than 50% accuracy.

Similarly, I elected not to implement the Splurges extension either.  I could imagine some contrived examples where it might be useful (for example, in sample.map, it is impossible to connect the two mines against a determined opponent; however, it can be forced by passing on the first move, and splurging on the secound).  But its utility seemed limited on multi-opponent maps, and even head-to-head there's rarely a shortage of good immediate moves to be played.  Prototyp seems to do just fine without it, and implementing it would have just been a waste of time.

The options extension, in contrast, was very clearly useful -- on a map like random 2, the first player to move will take the river between 70 and 79, and player 2 is just stuck; unable to join the mines on left hand side of the map with those on the right.  But with options, this was trivial to implement -- if options are enabled and Prototyp has one available, it will consider a claimed-but-unoptioned river as an available move (in the choke-finding search only).

