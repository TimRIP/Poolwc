new Vue({
	el: '#app',
	data() {
		return {
			info: []
		}
	},
	methods: {
		testpost: function (event) {

			var mytoken = localStorage.getItem('token')

			axios.defaults.headers.common['Authorization'] = mytoken

			axios
				.get("http://localhost:5000/api/test", {
					headers: {
						'Content-type': 'application/json; charset=utf-8'
					},
				})
				.then(response => {
					console.log(response)
					this.info = response.data
				})
				.catch(e => {
					alert(e)
				})

		}

	},

	mounted() {
		axios
			.get('https://api.coindesk.com/v1/bpi/currentprice.json')
			.then(response => {
				console.log(response)
				this.info = response
			})
	}
})


new Vue({
	el: '#vueformapp',
	data: function () {
		return {
			username: "timrip",
			password: "ug2-gj-8"
		}
	},
	methods: {
		dynamic: function (event) {
			alert("BUMB")
		}
		,
		apipost: function (event) {
			//alert(JSON.stringify(this.$data))

			var json = JSON.stringify(this.$data);
			//alert(json);

			axios
				.post("http://localhost:5000/api/token", json, {
					headers: {
						'Content-type': 'application/json; charset=utf-8'
					},
				})
				.then(function (response) {

					/*var mytoken = JSON.stringify(response.data.token);
		
					if (mytoken.startsWith("\"")) {
					 mytoken = mytoken.substring(1, mytoken.length);
					}
					if (mytoken.endsWith("\"")) {
					 mytoken = mytoken.substring(0, mytoken.length - 1);
					}*/

					const AuthStr = `Bearer ${response.data.token}`
					alert(AuthStr);

					localStorage.setItem('token', AuthStr);

				})
				.catch(e => {
					alert(e)
				})
		}
	}
})

var match = new Vue({
	el: '#vuematch',
	data: {
		TurnamentId: "0"
	},
	methods: {
		dynamic: function (event) {
			alert(this.$data.TurnamentId)
			match.apipostmatches();
		},
		apipostmatches: function (event) {
		  var mytoken = localStorage.getItem('token');
		  axios.defaults.headers.common['Authorization'] = mytoken;

		  const tid = Number(this.$data.TurnamentId);

		  if (!Number.isInteger(tid)) {
			alert("TurnamentId must be an integer. Got: " + this.$data.TurnamentId);
			return;
		  }

		  axios.post(
			"http://localhost:5000/api/matches",
			tid, // ✅ raw number -> JSON: 227
			{ headers: { 'Content-Type': 'application/json' } }
		  )
		  .then(function (response) {
			match.TurnamentId = response.data.matches;
		  })
		  .catch(err => {
			  console.log("AXIOS ERROR", err);

			  if (err.response) {
				console.log("STATUS:", err.response.status);
				console.log("DATA:", err.response.data);
				console.log("HEADERS:", err.response.headers);
				alert(
				  "Status: " + err.response.status + "\n" +
				  "Response: " + JSON.stringify(err.response.data, null, 2)
				);
			  } else {
				alert(err.message || String(err));
			  }
			});
		}
	}
})



var dash = new Vue({
	el: '#dashboard',
	data: {
		formdata: {
			errors: [
				{
					type: 0,
					description: "Vælg antal spillere"
				}
			],
			tournamentname: "new tournament",
			tournamentplayers: "3",
			rundearray: [
				{
					navn: "runde nr.1",
					NbPlayers: "3",

					//valsArray : [],
					//selected: "",
					//puljesize: "",
				}
			]
		}
	}
	,
	methods: {
		apipostturnament: function (event) {
			//alert(JSON.stringify(this.$data))

			var json = JSON.stringify(this.$data.formdata);
			//alert(json);		

			var mytoken = localStorage.getItem('token')
			axios.defaults.headers.common['Authorization'] = mytoken

	
			axios
				.post("http://localhost:5000/api/tournament", JSON.stringify(json), {
					headers: {
						'Content-type': 'application/json; charset=utf-8'
					},
				})
				.then(function (response) {


					match.TurnamentId = response.data.tournament;
					match.dynamic();

					ShowMyDiv("vuematch");
					//alert(response.data.tournament);
				})
				.catch(e => {
					alert(e)
				})
		},
		removeerror: function (type) {
			var len = dash.formdata.errors.length;
			var i;
			for (i = 0; i < len; i++) {
				if (dash.formdata.errors[i].type == type) {
					dash.formdata.errors.splice(i, 1);
					len--;
					i--;
				}
			}

		},
		addrunde: function (event) {
			this.formdata.rundearray.push({ navn: 'runde nr.' + (this.formdata.rundearray.length + 1) })
			this.onChangeNbPlayers(event);
		},
		removerunde(index) {
			this.formdata.rundearray.splice(index, 1)
		},
		openStorage() {
			return JSON.parse(localStorage.getItem('form'))
		},
		saveStorage(form) {
			localStorage.setItem('form', JSON.stringify(form))
		},
		loaddatapost: function (event) {
			dash.$data.formdata = this.openStorage();
			this.onChangeNbPlayers(event);
		},
		getdatapost: function (event) {
			this.saveStorage(dash.$data.formdata);

		},
		onChangeNbPlayers(event) {

			//We remove initial error
			//this.$set(dash.formdata.rundearray[0], 'NbPlayers' , event.target.value)
			this.$set(dash.formdata.rundearray[0], 'NbPlayers', dash.formdata.tournamentplayers)


			this.removeerror(0);
			this.removeerror(1);
			this.removeerror(2);
			//run through runder and calculate NbPlayers	
			for (var j = 1; j < dash.formdata.rundearray.length; j++) {
				var nb = 0;
				var k;
				for (k = j - 1; k >= 0; k--) {
					if (dash.formdata.rundearray[k].selected == "pool") {
						var nbinpool = 0;
						for (var i = 0; i < dash.formdata.rundearray[k].valsArray.length; i++) {
							if (dash.formdata.rundearray[k].valsArray[i] == j) {
								nbinpool++;
							}
						}

						nb += (Math.floor(dash.formdata.rundearray[k].NbPlayers / dash.formdata.rundearray[k].puljesize) * nbinpool)

					} else if (dash.formdata.rundearray[k].selected == "knockout") {
						if (dash.formdata.rundearray[k].valsArray[0] == j) {
							nb += (Math.floor(dash.formdata.rundearray[k].NbPlayers / 2))
						}
						if (dash.formdata.rundearray[k].valsArray[1] == j) {
							nb += (Math.floor(dash.formdata.rundearray[k].NbPlayers / 2))
						}
					}
				}
				this.$set(dash.formdata.rundearray[j], 'NbPlayers', nb)

			}

			//Check Players vs Pool Size!!
			for (var j = 0; j < dash.formdata.rundearray.length; j++) {
				if (dash.formdata.rundearray[j].selected == "pool") {
					if (dash.formdata.rundearray[j].NbPlayers % dash.formdata.rundearray[j].puljesize != 0) {
						dash.formdata.errors.push({ type: 1, description: 'pulje størrelse(' + dash.formdata.rundearray[j].puljesize + ') i runde' + (j + 1) + ', går ikke op i antal spillere(' + dash.formdata.rundearray[j].NbPlayers + ')!' })
					}
				}
				else if (dash.formdata.rundearray[j].selected == "knockout") {
					if (dash.formdata.rundearray[j].NbPlayers % 2 != 0) {
						dash.formdata.errors.push({ type: 1, description: 'knockout runde' + (j + 1) + ', med antal spillere(' + dash.formdata.rundearray[j].NbPlayers + ') går ikke op i 2!' })
					}
				}
			}
			//Check if Runde Type is selected
			for (var j = 0; j < dash.formdata.rundearray.length; j++) {
				if (dash.formdata.rundearray[j].selected == undefined) {
					dash.formdata.errors.push({ type: 2, description: 'runde' + (j + 1) + ', typen . . ikke valgt' })
				}

				if (dash.formdata.rundearray[j].BestOf == undefined) {
					dash.formdata.errors.push({ type: 2, description: 'runde' + (j + 1) + ', Bedst af . . ikke valgt' })
				}

				if (dash.formdata.rundearray[j].selected == "pool" && dash.formdata.rundearray[j].playstyle == undefined) {
					dash.formdata.errors.push({ type: 2, description: 'runde' + (j + 1) + ', afvikles som . . ikke valgt' })
				}
				if (dash.formdata.rundearray[j].selected == "pool" && dash.formdata.rundearray[j].playstyle == "swiss" && dash.formdata.rundearray[j].swissNb == undefined) {
					dash.formdata.errors.push({ type: 2, description: 'runde' + (j + 1) + ', swiss antal runder . . ikke valgt' })
				}
			}

			initCanvas();
		},
		onChange(event, index) {

			if (event.target.value == "pool") {
				this.$set(dash.formdata.rundearray[index], 'BestOf', 1)
				this.$set(dash.formdata.rundearray[index], 'puljesize', 4)
				this.$set(dash.formdata.rundearray[index], 'valsArray', [])
				var i;
				for (i = 0; i < dash.formdata.rundearray[index].puljesize; i++) {
					this.$set(dash.formdata.rundearray[index].valsArray, i, -1)
				}

				this.$set(dash.formdata.rundearray[index], 'playstyle', 'roundrobin')


			} else if (event.target.value == "knockout") {
				this.$set(dash.formdata.rundearray[index], 'BestOf', 1)
				this.$set(dash.formdata.rundearray[index], 'puljesize', 2)
				this.$set(dash.formdata.rundearray[index], 'valsArray', [])

				var i;
				for (i = 0; i < dash.formdata.rundearray[index].puljesize; i++) {
					this.$set(dash.formdata.rundearray[index].valsArray, i, -1)
				}

				this.$set(dash.formdata.rundearray[index], 'playstyle', 'roundrobin')
			}
			else {
				this.$set(dash.formdata.rundearray[index], 'BestOf', undefined)
				this.$set(dash.formdata.rundearray[index], 'puljesize', undefined)
				this.$set(dash.formdata.rundearray[index], 'valsArray', undefined)
				this.$set(dash.formdata.rundearray[index], 'playstyle', undefined)
			}

			this.onChangeNbPlayers(event);
		},
		onChangePlayStyle(event, index) {
			if (event.target.value == "beerpot") {
				this.$set(dash.formdata.rundearray[index], 'BestOf', undefined)
				this.$set(dash.formdata.rundearray[index], 'swissNb', undefined)
			} else if (event.target.value == "roundrobin") {
				this.$set(dash.formdata.rundearray[index], 'BestOf', 1)
				this.$set(dash.formdata.rundearray[index], 'swissNb', undefined)
			} else if (event.target.value == "swiss") {
				this.$set(dash.formdata.rundearray[index], 'BestOf', 1)
				this.$set(dash.formdata.rundearray[index], 'swissNb', 1)
			}
			onChangeNbPlayers(event);
		},
		onChangeNumber(event, index) {
			this.formdata.rundearray[index].valsArray.splice(event.target.value, (this.formdata.rundearray[index].valsArray.length - event.target.value))

			var i;
			for (i = 0; i < dash.formdata.rundearray[index].puljesize; i++) {
				if (dash.formdata.rundearray[index].valsArray[i] == undefined || dash.formdata.rundearray[index].valsArray[i] == null) {
					this.$set(dash.formdata.rundearray[index].valsArray, i, -1)
				}
			}
			this.onChangeNbPlayers(event);
		},
		onChangeDirection(event) {
			this.onChangeNbPlayers(event);
		}
	}
})

function initCanvas() {

	var ctx = document.getElementById("canvas").getContext("2d");
	ctx.fillStyle = "black";
	ctx.clearRect(0, 0, canvas.width, canvas.height);
	ctx.beginPath();
	ctx.rect(0, 0, 1500, 1500);
	ctx.stroke();

	ctx.font = "10px Comic Sans MS";
	ctx.fillStyle = "black";
	ctx.textAlign = "left";
	ctx.fillText("Tournament: " + dash.formdata.tournamentname, 5, 10);

	var setspace = { "px": 30, "playersize": 25, "space_between_player": 5, "space_between_puljer": 20, "defaultHight": 70, "space_between_runder": 15, "initialHight": 20 };

	let points = [];


	for (var i = 0; i < dash.formdata.rundearray.length; i++) {
		var rundespace = ((setspace.playersize + setspace.space_between_player)
		) * dash.formdata.rundearray[i].NbPlayers + (dash.formdata.rundearray[i].NbPlayers / dash.formdata.rundearray[i].puljesize) * setspace.space_between_puljer;

		var overflow = 380;
		var px = setspace.px;
		var py = (setspace.defaultHight + setspace.space_between_runder) * (i) + setspace.initialHight;
		var pwidth = rundespace; //owerflow => ...
		var phight = setspace.defaultHight;

		//runderne
		if (overflow < px + pwidth) {
			ctx.rect(px, py, overflow - px + 100, phight);
			pwidth = overflow - px + 100;
			//truncated graphic
			ctx.moveTo(px + (overflow - px + 10) , py + 12);
			ctx.lineTo(px + (overflow - px + 10) + 10 , py + 23);
			ctx.lineTo(px + (overflow - px + 10) , py + 34);
			ctx.lineTo(px + (overflow - px + 10) + 10 , py + 45);
			ctx.lineTo(px + (overflow - px + 10) , py + 56);

			ctx.moveTo(10 + px + (overflow - px + 10) , py + 12);
			ctx.lineTo(10 + px + (overflow - px + 10) + 10 , py + 23);
			ctx.lineTo(10 + px + (overflow - px + 10) , py + 34);
			ctx.lineTo(10 + px + (overflow - px + 10) + 10 , py + 45);
			ctx.lineTo(10 + px + (overflow - px + 10) , py + 56);
			
			ctx.fillText("Spillere i alt:", 25 + px + (overflow - px + 10), py + 30);
			ctx.fillText(dash.formdata.rundearray[i].NbPlayers, 25 + px + (overflow - px + 10), py + 45);

		} else {
			ctx.rect(px, py, pwidth, phight);
		}

		var p = { "x": px + pwidth, "y": py + phight / 2, "up": 0, "down": 0 };
		points.push(p);

		ctx.fillText(dash.formdata.rundearray[i].navn, setspace.px, (setspace.defaultHight + setspace.space_between_runder) * (i) + 10 + setspace.initialHight);

		for (var j = 0; j < dash.formdata.rundearray[i].NbPlayers / dash.formdata.rundearray[i].puljesize; j++) {

			//Puljerne
			var myx = px + 10 + j * setspace.space_between_puljer + (j * dash.formdata.rundearray[i].puljesize * (setspace.playersize + setspace.space_between_player));
			var mwidth = dash.formdata.rundearray[i].puljesize * (setspace.playersize + setspace.space_between_player) + 5;
			if(overflow < myx){
				//console.log("Removed");
			}
			else if (overflow <= myx + mwidth) {
				//console.log("length: " + Math.abs(overflow - myx) + " myx: " + myx + " mwidth: " + mwidth);
				ctx.rect(myx, py + 10, Math.abs(overflow - myx) + 5, phight - 20);
				
			} else {
				ctx.rect(myx, py + 10, mwidth, phight - 20);
			}

			//Spillerne
			var xx = myx + 5;
			var yy = py + 15;
			var ww = setspace.playersize;

			for (let k = 0; k < dash.formdata.rundearray[i].puljesize; k++) {

				var plx = xx + k * (setspace.space_between_player + ww);
				if (overflow >= plx + ww) {

					if (dash.formdata.rundearray[i].valsArray[k] == -1) {
						ctx.fillRect(plx, yy, ww, 40);
						ctx.font = "25px Arial";
						ctx.fillStyle = "white";
						ctx.fillText("X", plx + 4, yy + 30);
						ctx.font = "10px Comic Sans MS";
						ctx.fillStyle = "black";
					} else {
						ctx.rect(plx, yy, ww, 40);
					}
				}
			}
		}
	}

	var shift = 1;
	while (shift <= points.length) {


		for (var l = 0; l < (points.length - shift); l++) {

			var pressent = false;
			var text = "spiller: ";
			//Loop through RundeArray decide if draw line
			for (let r = 0; r < dash.formdata.rundearray[l].valsArray.length; r++) {
				if (dash.formdata.rundearray[l].valsArray[r] == l + shift) {
					if (!pressent) {
						text = text + (r + 1);
					}
					else {
						text = text + ", " + (r + 1);
					}
					pressent = true;
				}
			}

			if (pressent) {

				//ctx.beginPath(); // Start a new path
				ctx.moveTo(points[l].x, points[l].y + (phight / 2) - 5 - (points[l].down * 10));


				var maxx = 0;
				if (points[l].x > points[l + shift].x) {
					maxx = points[l].x;
				}
				else {
					maxx = points[l + shift].x;
				}
				ctx.lineTo(maxx + 10 + (points[l].down * 15), points[l].y + (phight / 2) - 5 - (points[l].down * 10));

				ctx.lineTo(maxx + 10 + (points[l].down * 15), points[l + shift].y - (phight / 2) + 5 + (points[l + shift].up * 10));

				ctx.lineTo(points[l + shift].x, points[l + shift].y - (phight / 2) + 5 + (points[l + shift].up * 10));


				//litlle arrow
				ctx.lineTo(points[l + shift].x + 7, points[l + shift].y - (phight / 2) + 5 + (points[l + shift].up * 10) - 5);
				ctx.moveTo(points[l + shift].x, points[l + shift].y - (phight / 2) + 5 + (points[l + shift].up * 10));
				ctx.lineTo(points[l + shift].x + 7, points[l + shift].y - (phight / 2) + 5 + (points[l + shift].up * 10) + 5);


				//Fill text out
				ctx.fillText(text, maxx + 10 + (points[l].down * 15) + 2, (points[l].y + (phight / 2) - 5 - (points[l].down * 10)) + ((points[l + shift].y - (phight / 2) + 5 + (points[l + shift].up * 10)) - (points[l].y + (phight / 2) - 5 - (points[l].down * 10))) / 2);

				points[l].down++;
				points[l + shift].up++;

				ctx.stroke(); // Render the path

			}

			//ctx.rect(points[l].x,points[l].y,2,2 );
		}

		shift++;

	}

	ctx.stroke();

}

window.addEventListener('load', function (event) {
	initCanvas();
});



function draw_square() {
	for (i = 0; i < dash.formdata.rundearray.length; i++) {
		var draw = SVG().addTo('#dashboard').size(300, 300)
		var rect = draw.rect(50, 50).attr({ fill: '#f07' })
	}
}

function ShowMyDiv(mydiv) {

	//$("div.mainContent").hide();

	var divs = $("#dashboard, #app, #vueformapp, #vuematch")

	divs.not(("#" + $(mydiv).attr("class"))).hide();
	$("#" + mydiv).show();
	$('.admin-panel .slidebar li').removeClass('selectedbutton');
	$("#" + mydiv + "Item").addClass('selectedbutton');

}

$(document).ready(function () {

	$("div.mainContent").hide();

	var divs = $("#dashboard, #app, #vueformapp, #vuematch")

	$(".admin-panel .slidebar li a").click(function () {
		$("div.mainContent").fadeIn().show();
		$("#" + $(this).attr("class")).fadeIn().show();
		divs.not(("#" + $(this).attr("class"))).hide();

	});

	$('.admin-panel .slidebar li').on('click', function () {
		$('.admin-panel .slidebar li').removeClass('selectedbutton');

		$("#dashboardItem").removeClass('selectedbutton');
		$("#appItem").removeClass('selectedbutton');
		$("#vueformappItem").removeClass('selectedbutton');
		$("#vuematchItem").removeClass('selectedbutton');

		$(this).addClass('selectedbutton');
	});
});
