
![favicon](https://github.com/user-attachments/assets/65727c13-a402-45c4-907c-3bc3ef7bbca7)

# Solis Manager - Automated Battery Management for Solis Inverters and Octopus Tariffs

This app is designed to optimally manage the battery charging for your Solar/PV/Battery system, when
used with the Octopus Smart tariffs. The idea is that it will analyse upcoming prices for Agile, Cosy, Go, etc, 
and then apply an opinionated strategy to manage your battery based on the cheapest periods.

### Features:

* Manages the charging (and discharging) of your battery to ensure optimal use of Octopus Tariffs
* Adaptive desktop and mobile UI, with Dark Mode
* Cross platform (runs on Linux, Windows, Mac, RaspPi, and docker). Very simple installation and setup
* Works with all Octopus Smart Tariffs (Agile, Cosy, Intelligent Go, Go, Flux, etc)
* SolCast PV forecasting to add charge/discharge strategy
* Costs view showing day-by-day whether your system is in profit or loss
* Scheduled actions let you charge/discharge/hold the battery SOC for particular times every day
* Simple manual overrides - Charge or Discharge your battery with a simple click (no SolisCloud login/password)
* Comparison Feature to track relative prices between your current tariff and alternatives, for tariff-hoppers
* Auto-Detection of Tariff Switches with auto-adjusting charging strategy
* 'Dump-And-Recharge' feature for when Agile prices go negative
* History view showing forecast/actual PV generation
* Simulation Mode, so you can see how the charging strategy will work as you step through the day

### Referral Link

If you don't already use Octopus, but like the sound of this app and want to sign up to an Octopus tariff,
please use my referral link, and we'll both get £50!

* [https://share.octopus.energy/wise-dog-4](https://share.octopus.energy/wise-dog-4)

### Screenshots:

<img width="1168" alt="PlanTable" src="https://github.com/user-attachments/assets/46c927e5-2d2e-4c29-a19b-a2bde24155b2" />

<img width="1176" alt="PlanGraph" src="https://github.com/user-attachments/assets/a17d3445-b2cf-45c1-ac3c-60d92edeb5ef" />

#### Dark Mode 

<img width="1169" alt="DarkMode" src="https://github.com/user-attachments/assets/2d67171e-b442-454f-8fd2-5b22a9e98579" />

### Mobile: 

<img width="300" src="https://github.com/user-attachments/assets/946e6a3b-4261-4915-8ebf-3ff4b0e69db1"/>
<img width="300" src="https://github.com/user-attachments/assets/8bce3207-1c9d-4047-a5ff-f1540e288521"/>
<img width="300" src="https://github.com/user-attachments/assets/f9f5e92b-a881-43ec-87af-16d83e195322"/>

### Warranty / Disclaimer 

**PLEASE NOTE:** This application is provided as-is, with no guarantees; I accept no liability for any issues that arise with 
your electricity bill, inverter, battery, or any other system, as a result of using this application. **Your
choice to run the app is entirely at your own risk.**

### Security Warning

 **Warning**: This application should **never** be exposed to the internet via port-forwarding or other public access.
 Solis Agile Manager does not have any authentication, which means that a malicious hacker could easily control your 
 Inverter.

If you want to be able to access the application remotely, please ensure you only do so via a VPN or a reverse proxy.

## Installation

SolisManager runs as a server-based app, and is designed to run 24/7, in the background, with minimal 
interaction from the user. If it's working well, you shouldn't have to do anything once it's set up.

### Running as a local app on Linux/Mac/Windows/Raspberry Pi

To run it, go to the [latest release on GitHub](https://github.com/Webreaper/SolisAgileManager/releases/latest), 
and download the appropriate package for the operating systtem you're going to use (Mac/Linux/Windows/RPi).
Extract the zip into a folder and then run the main executable. Note that the config file and logs etc 
will be written to a folder called `Config` in the current working directory. If you'd rather they were
written somewhere else, pass your chosen folder as the first command-line parameter. 

Once the server is running, navigate to the UI via your browser. It will be at `http://localhost:5169`.

### Running via Docker 

You will need to pull the `webreaper/solisagilemanager` container, and map the internal port `5169` to the port
you want the web-UI exposed on from your host. You will also need to map a volume `/appdata` to a writeable
local folder on your host, so that the config, and log files are written outside the container (otherwise
when you pull a new version or recreate the container, you'll have to set up from scratch again).

Here's a sample `docker-compose` entry:

```
   solismanager:
        container_name: solismanager
        image: webreaper/solisagilemanager:latest
        ports:
            - '5169:5169'
        restart: unless-stopped
        environment:
            - TZ=Europe/London
        volumes:
            - /volume1/dockerdata/solismanager:/appdata
```

Note that the `TZ` environment variable is important - if you don't specify this you could end up with
the SolisManager docker container running on GMT, and the inverter running on BST, which would give 
bad results.

#### Supported Docker Platforms

Multiplatform Docker Images are available for:

* linux/amd64 - for x64 Intel processors
* linux/arm64 - for ARM64 processors
* linux/arm - ARM processors

These images should work for Linux, Mac, Windows and Raspberry Pi. 

## Note for users with Inverters running newer Firmware

If you start running SolisManager and find that the inverter charging is not being applied correctly, it's 
likely that you're on the newer firmware which has an enable/disable switch for each charge/discharge slot:

<img width="400" alt="image" src="https://github.com/user-attachments/assets/55de1fdd-0cd0-40b7-9767-c38664423f31" />

SolisManager does not enable the charge slots automatically yet (I am planning to add that in a future release)
so you will have to enable the `Charge Slot 1` and `Discharge Slot 1` controls. You only need to do this once.

For more details, please see https://github.com/Webreaper/SolisAgileManager/issues/203

## Settings

The first time you load the UI, you'll be prompted to input basic information, such as your Solis API key and 
secret, your inverter serial number, and the Octopus Product details of the current tariff you're on. 

<img width="636" alt="Settings" src="https://github.com/user-attachments/assets/d2dcf972-7676-4230-827d-01b36eefc02c" />

<img width="633" alt="Settings2" src="https://github.com/user-attachments/assets/6d7d1cf2-8762-45a9-ae87-15cbd1e9cbd7" />

#### Octopus Tariff Setup

The app will connect to your Octopus account and find the current tariff that you're using. 

* Enter the Account number and Octopus API Key into the settings. When you click 'Save config' the tariff
  details will be pulled from your Octopus Account

Once the API key and account are configured, the application will query every 4 hours to check if your tariff
has changed, and update accordingly. So if you change tariff (e.g., switch from Agile to Cosy) you should 
start seeing the new tariff prices flow into the app within 4 hours.

Note: If you have two account numbers - one for your import meter and one for your export meter, enter them
into the 'account number' field, separated by commas (e.g., `A-123456,A-234567`). Solis Manager will use the two
accounts to pull the import and export data for the Costs page (see below).

#### Manual Tariff Settings 

If you don't want to use your Octopus account to infer the current tariff, you can enter the product and tariff
product code manually. Just leave the Octopus API key and Account number blank and enter the product and tariff 
codes yourself. 

* [Click here](https://api.octopus.energy/v1/products/) to get the list of Octopus Agile Product codes (e.g.,
  `AGILE-24-10-01`).
* Then once you've found your tariff, click on the URL in the `links` section to find your region-specific
  product code. For example: [https://api.octopus.energy/v1/products/AGILE-24-10-01/](https://api.octopus.energy/v1/products/AGILE-24-10-01/)
* From that page, copy the product code (e.g., `E-1R-AGILE-24-10-01-A`) into the Solis Manager settings.

Note that in the most common cases, selecting `AGILE-24-10-01` and `E-1R-AGILE-24-10-01-A`, and just altering 
the last `A` to the correct [Region Code](https://mysmartenergy.uk/Electricity-Region) will do exactly what you
need. Solis Manager doesn't do anything with the standing charge, so it doesn't matter if you're on an older
tariff. 

### Other Configuration Settings

You'll also need to set some other config settings that control the way the charging plan works:

* Max Charge Rate in Amps - set to the level that your battery can charge/discharge at. You should refer
  to your inverter/installer to check what is the max safe charging rate for your system's battery. 

* Charge slots for full battery - which tells the app how many slots of charging will be needed to go from
  empty to full. This will depend on your battery size and charging rate. Eventually the app will calculate
  this based on historical charging data, but for now, it's a manual setting.

* `Battery Boost Threshold` - the percentage at which you'd like to boost charge if prices are a bit lower
  than average

* The `Always charge below` rate. For example, if you set this to 10p/kWh, then _any_ slot lower than that
  price will always be set to charge, regardless of anything else. Can be useful to ensure you prioritise
  export, for example.
  This can be useful for some tariffs - e.g. if you are on Cosy, which has alternating periods of high and
  low prices, set is value to just above the cheap price and it will guarantee that your battery always
  charges in the pricing 'troughs'. I set it to 15p/kWh when I'm on Cosy.

* The `Charge if SOC below %` - this setting will maintain a particular minimum battery level. So for example,
  if you set it to 35%, then any time the battery falls below 35% at the start of a slot, that slot will be
  marked to Charge, regardless of price or PV availability.
  Note - currently this is only checked once every 30 minutes (at the start of each slot). This will be made
  more granular in future.

* `Battery %age for peak period`: set this is an approximation of how much battery you need to get you
  through the peak period of 4pm-7pm. If you have a small battery and use a lot of power in the afternoon,
  you might want this to be 100% - so it'll allow enough charging to get to 100% before the peak time starts.
  
  For me, we usually only use about 5-6kWh between 3pm and 7pm; our battery is 14kWh, so I have it set to 60%. 
  The idea of this setting is that you want enough power to get through the peak period, but it doesn't 
  necessarily need to be fully-charged.

* Simulate-only - if checked, the app will run and simulate what it _would have done_ without actually making
  any changes to the behaviour.

### Octopus Costs / Profits View

The Costs screen allows you to see the electricity costs for your current tariffs - both import and export.
The table view shows the amount you've imported, and the average price you paid for that imported power. 
The exported columns show the price you were paid for the exported excess generation. The Net Cost figure
tells you whether you were in profit or loss on a given day. A red total indicates that you've had to pay
for some imported power; a green total indicates that you made money from your export that day.

![Costs Table](https://github.com/user-attachments/assets/a1c03edd-abab-4130-aa45-265d3adc9929)

Alternatively you can view the net cost/profit as chart. This view allows you to quickly see which days
cost you, and which days made you money. 

![Costs Graph](https://github.com/user-attachments/assets/5eb81bc0-5399-41ce-a90e-d0a86fc5449f)

You can select the timespan for the table and graph to view up to 6 months of history - note that longer
time periods involve a lot of data, so can take quite a few API calls and time to process the totals.

The costs screen also has a 'Compare' function - a little bit like the well-known Octopus Compare app. 
If you check the 'Compare' checkbox, and select alternate tariffs, you can see what the relative cost 
difference would have been - for your exact historical consumption/export - for those tariffs. It's a
good way of checking that you're on the optimal tariff.

<img width="1517" height="960" alt="Costs Screen" src="https://github.com/user-attachments/assets/4441448d-e140-460b-aa08-2f5c1bd485fa" />

### Intelligent Go Dispatch Management

** See Note Below **

Octopus Intelligent Go is an advanced tariff that manages your Electric Vehicle (EV) charging automatically. 
One advantage it provides is that if you need to charge your EV at any time of day to prepare for a journey,
you can call for an 'IOG Dispatch' which will charge the vehicle at the cheap rate (7p/kWh or so) at *any* 
time of day. 

So even if you're in the normal peak period of 4pm-7pm, you get a charge slot at a significantly
reduced rates - and because of the way smart-meters work, this means that the electricity for the entire house
is also provided at that cheap rate for the duration of the IOG Dispatch. 

Solis Manager now detects these cheap slots and can be configured to always charge the house battery while 
they are in progress - giving you a cheap boost to your battery at the reduced IOG rate. During the IOG
Dispatch period, Solis Manager checks the state every 5 minutes to ensure the cheap charge rate is still 
available, and will cancel the house charge if it ends (because, for example, the EV reaches 100% charge,
or you unplug your EV from the charger).

![IOGSlots](https://github.com/user-attachments/assets/41a3b945-a6e5-4598-a420-a818a8bebf78)

#### Important Note about IOG Dispatch Charging

As of April 2025 It seems that Octopus are introducing changes to their Intelligent Octopus Go tariff T&Cs. 
Now when you car is charging outside the cheap rate period, they only intend to apply the 7p tariff to the 
power used to charge your car, not the whole house supply as it was previously.

This means that the IOG dispatch feature may not give you cheap electricity to charge the house - please 
check your T&Cs - I cannot be responsible for unexpected bills from charging during IOG dispatches.

The feature may still be useful though: if you set the IOG Charge Amps to zero in the Solis Manager config,
this should change the behaviour to apply a zero-amp charge whenever an IOG dispatch is in progress, which
should force the EV to charge from the grid, and therefore avoid the EV being charged from the house battery.

As of Dec 2025 I added an option so you can choose whether to use the full 30-minute slot for charging the 
house or whether the action will end directly when the IOG smart charge period ends. 

#### How does it work?

First, check the 'Intelligent Go Charging' checkbox in the Settings screen, and save the settings.

Then if Octopus sends you a smart-charge slot you'll see something like this in the logs:

```
  Found 2 IOG Smart-Charge slots (out of a total of 2 planned and 3 completed dispatches)"
     Time: 16:34 - 17:26, Type: smart-charge, Delta: -7"
     Time: 18:22 - 18:55, Type: smart-charge, Delta: -9"
```
In the above case, you'd see the slots in the Charging plan change to a car icon for:

- 16:30-17:00
- 17:00-17:30
- 18:00-18:30
- 18:30-19:00

and the inverter should charge the house battery for that period. Note that smart-charge slots don't seem
to be reliably sent to the API, so it's possible you might find yourself charging your EV in a cheap slot
during the day, but SolisManager doesn't get notified of them, so isn't able to take advantage of them
and charge your home battery. Unfortunately, that seems to be a foible of some chargers/cars, and is 
beyond my control!

### Avoiding Time Drift

The application has a setting that will, every day at 2am, update the inverter time to match internet time.
This can fix the natural time-drift of the inverter's clock, and ensure your charging happens at the right 
times. 

If you'd rather not have this feature, you can disable it in the config. 

### Scheduled Actions

A scheduled action allows you to specify a particular action that will always be applied for a particular 
time of day (i.e., for a particular slot). You can add multiple scheduled actions, and they will take 
precedence over all other rules except for the `Charge if SOC less than %` option.

An example use case for this is as follows: 
* You have a large battery that easily covers your house load for the entire day
* You have an overnight charge rate of 7p/kWh from 11pm - 5am. 
* You are on a fixed export rate of 15p/kWh (so cheaper than the overnight charge rate)
* You configure 3 Scheduled actions to `Discharge` at 20:30, 21:00 and 21:30 - to export any unusage battery 
  charge to the grid, earning 15p/kWh
* Then your normal charging plan charges the battery up at 7p/kWh in the early hours of the morning
* This means you earn a net 8p/kWh for every unused unit of charge in your battery at the end of the day.

Each scheduled action can use an SOC threshold to govern when it's applied. For example, you can set an action
to charge while the SOC is below a specific percentage, or discharge while SOC is above a percentage - allowing you
to create a rule that says `at 8pm each night, discharge my battery if it has more than 30% charge`. You can also 
override the default/global settings for charge/discharge Amps, so scheduled actions can charge/discharge at a different
rate.

<img width="1032" alt="ScheduledActions" src="https://github.com/user-attachments/assets/519163d8-d254-4da0-b8d0-47cefcc9370e" />

### Getting Inverter Control Permission from Solis 

**Note**: For this app to work, you'll need to have raised a ticket with Solis to get access to control the 
inverter via the SolisCloud app. To do this:

* Go to the [Solis Support Portal](https://solis-service.solisinverters.com/en/support/solutions)
* Click 'Submit a ticket'
* Submit a request to control your inverter from the SolisCloud App, filling in the details and selecting
  ticket type `API Request - Owner`. In the notes, ask for API access too.

### Comparison Tool

For people who like to tariff-hop to ensure they get the best price, the app now has a comparison tool that 
will show a chart of upcoming prices for your current tariff, versus other Octopus products.

<img width="1202" alt="Screenshot 2025-01-27 at 22 26 20" src="https://github.com/user-attachments/assets/6d4f8e32-bbc5-4423-9ba0-401816202c5c" />

### History Data

Once you've filled these in, the server will start running.

As it runs, the last 30 days' worth of charging decisions will be logged to `SolisManagerExecutionHistory.csv` 
so you can monitor the decisions it's taking to ensure they're as you require. There's also a History page that 
allows you to convienently check what it did, and why it did it:

<img width="1411" alt="HistoryView" src="https://github.com/user-attachments/assets/42078bb4-6f47-4adc-8f66-0f2f0c8e2eed" />
  
### Will the app work with non-Agile Tariffs?

Yes! The latest version has been updated to work with all Octopus Smart Tariffs. For example, the screenshot
below shows the charging plan for Cosy - with the `Always Charge Above` config setting set to 15p/kWh.

<img width="1252" alt="Screenshot 2025-01-22 at 07 18 28" src="https://github.com/user-attachments/assets/36d1e49a-c9d8-4549-b414-d3a4cbbd34ad" />
  
### How does the app work?

At first launch SolisManager will load the next set of Agile Tariff data, along with some information about
your inverter. It will then estimate the best charging strategy based on a number of rules, as set out below. 
Note that this strategy is based on my needs for battery-management, but should apply to many other people too.

### Who Will It Work For?

This application is based on a number of assumptions, the primary one of which is that the person running it 
is a high-consumption power user, probably with an Air Source Heat Pump or EV, and wants to optimise their 
battery charging to charge at the cheapest times. The goal of the app is to charge the battery at the cheapest
times possible, without too much unnecessary battery cycling. If you are a lower-useage household, you may find
that Rob Tweed's [Agility](https://github.com/robtweed/agility) app is better suited to your needs.

### The Algorithm/Strategy

* First, find the cheapest period for charging the battery. This is the set of contiguous
  slots, long enough when combined that they can charge the battery from empty to full, and
  that has the cheapest average price for that period. This will typically be around 1am in 
  the morning, but can shift around a bit.
* Then perform a similar calculation to find the most expensive (peak) period.
* Set the action for all cheapest slots to `Charge`
* Set the action for the most expensive slots to `Do Nothing` - i.e., don't charge.
* Set the action for any negative prices slots to `Charge`.
* Now, we've calculated the cheapest and most expensive slots. From the remaining slots, calculate
  the average rate across them. We then use that average rate to determine if any other slots across
  the day are a bit cheaper. So look for anything that's 90% of the average or less, and mark it
  as `BelowAverage`. For those slots, if the battery is low, we'll take the opportunity to charge as 
  they're a bit cheaper-than-average, so set their action to `Charge If Low Battery`.
* If we have a set of cheapest slots, then the price will usually start to drop a few slots before
  it's actually cheapest; these will likely be slots that are BelowAverage pricing in the run-up to
  the cheapest period. However, we don't want to charge then, because otherwise by the time we
  get to the cheapest period, the battery will be full. So back up `n` slots and even if they're
  `BelowAverage`, set them to `Do Nothing`.
* If we have a set of priciest slots, we want to ensure the battery is charged before we reach them.
  So work backwards from the first expensive slot, applying a charge instruction on each slot before
  the priciest, until we've got enough slots to fully charge the battery.
  Note that we skip an extra one, because the slot before the priciest ones is almost always a bit more
  expensive too. Just in case it's REALLY expensive, allow ourselves a couple of steps back to look
  for slightly cheaper rates. Can't go back too far though otherwise the battery might not last through
  the peak period.
* If there are any slots below our "_Blimey it's cheap_" threshold, elect to charge them anyway. E.g.,
  we may elect to always charge if the price is negative, or if it's below our export rate.
* For any slots that are set to `Charge If Low Battery`, update them to 'charge' if the battery SOC is,
  indeed, low. Only do this for enough slots to fully charge the battery.
* Lastly, find runs of slots that have negative prices. For any groups that are more than long enough
  to charge the battery fully, discharge the battery for all the slots that aren't needed to recharge
  the battery. For example, we might end up with a run of 3 negative prices, and later another group of
  8 negative prices. If our battery takes 3 hours to fully charge, the first two negative slots of that
  group of 8 will be set to discharge the battery. See the chart below:

  <img width="909" alt="DischargeStrategy" src="https://github.com/user-attachments/assets/a9f89ad7-b969-4e9d-bc38-569ef3ce8caa" />

There are also _manual overrides_ which can be set vie the tools screen. For example:

* Charge the Battery - this will override the next `n` slots, regardless of cost, to completely
  charge the battery
* Discharge the Battery - this will override the next `n` slots to completely discharge the battery
* By clicking the `x` next to any charge/discharge slot, you can apply an override which will cancel
  the charge action and return that slot to `Do Nothing`.

### More on Simulation Mode

To test the app and ensure it functions correctly, simulation mode shows what would have happened,
without actually writing changes to the inverter. This allows you to see how the charging plan changes
with the strategy as different information comes in. 

Simulation mode is interactive; the first time the app loads it initialises the simulation using the
current inverter state, and the loaded Agile prices. As you advance through the simulation, it shows
what will happen (and the logs will show the commands that would have been sent to the inverter).

Once you run out of slots at the end of the simulation, click reset to start again.

### Solcast PV Forecast Data

The application can use a Hobbyist Rooftop account from [Solcast](https://solcast.com/free-rooftop-solar-forecasting) 
to estimate the likely PV yield from your system over the next day or so. 

To configure this add settings for:

* Your Solcast API key
* Your Solcast Rooftop Site ID (this is a four-part hex ID)
* A damping factor (see below)

Currently the Solcast data is only used for display purposes, but will eventually be used to optimise the 
algorithm (e.g., by skipping overnight charging if the forecast is for a decent PV yield.
  
#### Solcast Damping Factor

Solcast can often over-estimate the forecast for the PV yield, because it may under-estimate the cloud 
impact, or may not take into account panel shading, and string efficiency. Therefore, the config settings 
have a field for Solcast Damping Factor. So for example, if your Solcast forecast is generally 2x your
_actual_ PV yield, then set the damping factor to 50%. The damping factor is applied immediately to all
of the Solcast figures in the UI, so you can adjust this up/down until it matches reality.

In future, the application will look at historic PV yields from the inverter, and compare this to the 
Solcast forecast, and then auto-adjust to match reality.

### Technical Considerations

#### But why not just use the excellent [PredBat](https://springfall2008.github.io/batpred/) plugin for Home Assistant?

I spent quite a lot of time researching PredBat. It looks awesome, and I would love to run it. However, Solis support
for Predbat is quite limited, which makes it unsuitable for my needs. Specifically, there is no current way to run 
PredBat with a Solis Inverter, solely using the Solis API. This means that there are only two alternatives:

* Run the Solis Inverter and PredBat / Home Assistant using Modbus. The problem with that is that the Solis Wifi dongle
  cannot support Modbus _and_ the Solis API, at the same time. So running Predbat with Modbus means losing the SolisCloud
  application which is excellent for monitoring the inverter state.
* The only way to get ModBus working **and** continue to use SolisCloud, is to use custom hardware, and that's not a road
  I'm interested in going down.

There is another factor: Home Assistant can be an unwieldy platform to install and maintain. It's amazing in terms of what
it can do, and the community is extremely spirited, but hassle of keeping an instance updated (there's no auto-updates for
integrations and plugins) combined with the unfriendliness of configuration of many components, means that while I run HA,
I'm not a fan of it. In particular, for less technical users the idea of just installing an EXE or docker image and running
it without complex setup and configuration is very appealing.

#### Avoiding Solcast Rate-limiting

Solcast API calls are rate-limited to 10 API calls per day - after which the call will fail and no data is
returned. To avoid blowing through this limit the strategy is:

1. Attempt to retrieve Solcast data from the API at midnight, 6am, and midday (because the forecast can change 
   through the day)
2. If the API call succeeds, the results are written to a file `Solcast-latest.json` in the config folder.
3. If you restart the app it reads from that file if it exists.

This avoids the API throttling in most cases. Also, Solcast recommend not doing it on the hour (because 
otherwise everyone hits their API on the hour....) so the app actually makes the request at the somewhat
arbitrary times of 02:13, 06:13 and 12:13.

#### Using the Solcast Data

Currently this data is only used for display purposes. I haven't worked out how I'll use the data yet (there 
isn't enough PV to make a difference at the moment). Probably:

* Move the Pre peak morning charge earlier to prioritise export on days when the PV is going to be good
* Reduce overnight charging if the PV forecast for the next day is going to be good.

If you have other ideas or suggestions, let me know!

### Reducing Inverter EEPROM Writes

Solis inverters store the current configuration (such as charge / discharge times, charge rate Amps,
etc) in a writeable storage chip called an [EEPROM](https://en.wikipedia.org/wiki/EEPROM). Unfortunately,
EEPROMs usually have a limited lifespan based on the number of writes that can be made - once that 
limit is reached the EEPROM can become unreliable, with writes not 'sticking' and even some reads failing.

Newer Solis inverters use an EEPROM chip with a life-span of about 100,000 writes, which is enough to last
for a long time, as long as not too many writes are made. Some older inverters may have chips that have a 
lifespan of about 10,000 writes. Once the EEPROM chip reaches its end-of-life, the reliability might drop 
off significantly - and the only fix is to re-solder a new EEPROM chip onto the main inverter PCB. This 
may or may not be able to be done under warranty.

Depending on the operation, it may take multiple writes to achieve a particular outcome. For example, on 
older firmware, setting a charge slot (start and end time, and charge Amps) might only be a single write 
operation. With the newer six-slot Solis firmware, these parameters are set individually - which can mean
setting a charge or discharge might require 4-6 write operations. 

This limited lifespan can cause problems with applications that manage the inverter, particularly if they 
make a lot of updates. So, for example, if your inverter management software updates the charge time every
30 minutes, on an inverter with newer firmware it would be very easy to rack up 200+ writes per day - which
would total more than 70,000 writes per year. This could drastically reduce the lifespan of the inverter.

The problem is EEPROM write lifespan affects any application that updates the inverter slots using the 
inverter control API. This includes SolisManager, Home Assistant (PredBat, PV_Opt etc), Agility and even
the SolisCloud app and its EMS algorithm. The same limitations apply however the inverter is updated - so 
it's not just limited to writes sent via the Solis API; the same problem will happen if you make a lot of
updates via ModBus.

#### How does SolisManager avoid damaging your inverter with too many writes?

To minimise inverter writes, the app applies Charging, Discharging and 'no charge' instructions in 
batches. So for example, if the charging plan is as follows:

* 06:00-06:30 Do Nothing
* 06:30-07:00 Do Nothing
* 07:00-07:30 Charge
* 07:30-08:00 Charge
* 08:00-08:30 Charge
* 08:30-09:00 Charge
* 09:00-09:30 Do Nothing
* 09:30-10:00 Do Nothing

Then the actual calls are conflated to the following:

* 06:00 - Set inverter charge slot to 00:00-00:00 to turn off charging
* 07:00 - Set inverter charge slot to 07:00-09:00 to charge for 2 hours
* 09:00 - Set inverter charge slot to 00:00-00:00 to turn off charging

This optimisation means that the absolute minimum number of `control` API calls are made (from about 17,000 per
year down to around 2,000), and hence the minimum number of Inverter EEPROM writes are carried out.

#### Inverter State-Tracking

Another strategy that SolisManager uses to minimise writes is to track the state of the inverter charge/discharge
slots itself. Whenever any write operation is about to be carried out:

1. The current state of that command is read from the inverter
2. If the read succeeds, the state of that command is cached in an internal tracker
3. If for any reason the read fails (due to an inverter API failure or EEPROM read failure) the app 
   uses the cached state
4. Only if the existing value for the command doesn't match what's requested, is an actual write 
   command executed
5. If the write command is executed successfully, that value is stored in the internal tracker

By carefully tracking the state, and only updating the inverter settings when absolutely necessary, it's been 
shown that for most users, the maximum number of EEPROM writes will be less than 10 a day - which means for
a new inverter, the EEPROM should last for around 20-25 years. 

### Adding Support for Other Inverters

Although the app was originally developed for Solis Inverters, there's no reason why it can't support Other inverter
types. However, I won't be able to develop them because it's impossible to test - so would need others to collaborate
and contribute implementations for other inverters.

If you'd like to consider contributing, the steps are generally something like this:

1. Add a new project similar to the `SolisManager.Inverters.Solis` one in the project, for your inverter, which has 
   a class that implements the `IInverter` interface (with methods to set a charge, and retrieve SOC and other 
   state from the inverter).
2. Extend the `SolisManager.InverterFactory` class to return an instance of the `IInverter` implementation, based
   on the config type passed in.
3. Create a new config class for the inverter to collect and settings that are required, similar to `InverterConfigSolis`.
   You'll also need to add the `JsonDerivedType` attribute for the new type in `InverterConfigBase`.
4. Create a new component to collect the configuration, similar to the `SolisInverterConfig.razor` component.
5. Extend the `ConfigSettings.razor` `InverterSettings()` method to return the new config component

That should be most of what's required. I may create a couple of skeleton implementations for the more popular inverter
types to get people started....

### Other Things I've Written

If you like this app, please check out some of my other applications, including a Photo Management system 
    called <strong><a href="https://github.com/webreaper/damselfly" target="_blank">Damselfly</a></strong>

  Solis Agile Manager is free, open-source software. But if you find it useful, and fancy buying me a coffee or a 
  slice of pizza, that would be appreciated! You can do this via my Damselfly BuyMeACoffee link.

  <div>
      <a href="https://www.buymeacoffee.com/damselfly" target="_blank">
          <img src="https://cdn.buymeacoffee.com/buttons/arial-yellow.png" alt="Buy Me A Coffee" height="41" width="174">
      </a>
  </div>

### Thanks

* Thanks to [Steve Gal](https://github.com/stevegal/solis_control) for his Solis-Control script that gave me the 
  info needed to build the API wrapper to set the charging slots on the inverter.
* Thanks also to [Jon Glass](https://github.com/jmg48/solis-cloud) for his sample .Net wrapper for the Solis API,
  without which I'd have spent an inordinate amount of time figuring out the complicated Solis Authentication
  process.
* Thanks to Steve Mell, for his huge help (and amazing patience) in debugging and testing the application
* Thanks must go to [Rob Tweed](https://github.com/robtweed) whose Agility project made me think about building
  this application.

### Technical Details

For those who are interested, the application is built using Blazor WebAssembly, with an ASP.Net back-end. The app 
is written entirely in C# on a Mac, using .Net 9. The core functionality was written over the course of about 
a week. 

### Credits

* Thanks as always to the folks in the [MudBlazor](https://github.com/mudblazor/mudblazor) team, whose excellent
  suite of UI components makes developing Blazor UIs a dream.
* Thanks to James Hickey for the [Coravel](https://github.com/jamesmh/coravel) project, which made the job 
  scheduling in Solis Agile Manager trivial.
* Thanks to the [Blazor Apex Charts](https://github.com/apexcharts/Blazor-ApexCharts) developers - adding some 
  beautiful visualisations to the app was super-trivial with this library.
* Thanks to the folks at JetBrains for providing [Rider](https://www.jetbrains.com/rider/) for free to OSS 
  developers like me.
