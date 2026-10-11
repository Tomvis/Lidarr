import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { executeCommand } from 'Store/Actions/commandActions';
import { setTracksTableOption } from 'Store/Actions/trackActions';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import AlbumDetailsMedium from './AlbumDetailsMedium';

function createMapStateToProps() {
  return createSelector(
    (state, { mediumNumber }) => mediumNumber,
    (state, { albumReleaseId }) => albumReleaseId,
    (state) => state.tracks,
    createDimensionsSelector(),
    (mediumNumber, albumReleaseId, tracks, dimensions) => {

      // Tomvis fork (MUSIC-68): an edition's section shows only that edition's tracks
      const tracksInMedium = _.filter(tracks.items, albumReleaseId ? { mediumNumber, albumReleaseId } : { mediumNumber });
      const sortedTracks = _.orderBy(tracksInMedium, ['absoluteTrackNumber'], ['asc']);

      return {
        items: sortedTracks,
        columns: tracks.columns,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const mapDispatchToProps = {
  setTracksTableOption,
  executeCommand
};

class AlbumDetailsMediumConnector extends Component {

  //
  // Listeners

  onTableOptionChange = (payload) => {
    this.props.setTracksTableOption(payload);
  };

  //
  // Render

  render() {
    return (
      <AlbumDetailsMedium
        {...this.props}
        onTableOptionChange={this.onTableOptionChange}
      />
    );
  }
}

AlbumDetailsMediumConnector.propTypes = {
  albumId: PropTypes.number.isRequired,
  albumMonitored: PropTypes.bool.isRequired,
  albumReleaseDate: PropTypes.string,
  mediumNumber: PropTypes.number.isRequired,
  albumReleaseId: PropTypes.number,
  setTracksTableOption: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AlbumDetailsMediumConnector);
